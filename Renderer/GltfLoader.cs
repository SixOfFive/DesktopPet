using System.Drawing;
using System.Numerics;
using SharpGLTF.Schema2;
using Silk.NET.OpenGL;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;
using GLTexture = Neko.Renderer.Texture;
using SharpNode = SharpGLTF.Schema2.Node;

namespace Neko.Renderer;

internal static class GltfLoader
{
    public static AnimatedModel Load(GL gl, string path)
    {
        var model = ModelRoot.Load(path);
        var scene = model.DefaultScene ?? model.LogicalScenes[0];

        var sharpNodes = new List<SharpNode>();
        foreach (var root in scene.VisualChildren)
            WalkScene(root, sharpNodes);

        int nodeCount = sharpNodes.Count;
        var sharpIndex = new Dictionary<SharpNode, int>(nodeCount);
        for (int i = 0; i < nodeCount; i++)
            sharpIndex[sharpNodes[i]] = i;

        var nodes = new NodeData[nodeCount];
        var restWorld = new Matrix4x4[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            var src = sharpNodes[i];
            int parent = src.VisualParent != null && sharpIndex.TryGetValue(src.VisualParent, out var p) ? p : -1;
            var lt = src.LocalTransform;
            nodes[i] = new NodeData
            {
                ParentIndex = parent,
                Name = src.Name ?? string.Empty,
                RestT = lt.Translation,
                RestR = lt.Rotation,
                RestS = lt.Scale,
            };
            var local = Matrix4x4.CreateScale(lt.Scale)
                * Matrix4x4.CreateFromQuaternion(lt.Rotation)
                * Matrix4x4.CreateTranslation(lt.Translation);
            restWorld[i] = parent < 0 ? local : local * restWorld[parent];
        }

        var meshesByNode = new Dictionary<int, List<Mesh>>();
        var textureCache = new Dictionary<int, GLTexture>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int i = 0; i < nodeCount; i++)
        {
            var src = sharpNodes[i];
            if (src.Mesh == null) continue;
            var primList = new List<Mesh>();
            foreach (var primitive in src.Mesh.Primitives)
            {
                var built = BuildPrimitive(gl, primitive, textureCache, restWorld[i], ref min, ref max);
                if (built != null) primList.Add(built);
            }
            if (primList.Count > 0) meshesByNode[i] = primList;
        }

        if (meshesByNode.Count == 0)
            throw new InvalidOperationException($"No renderable meshes in '{path}'");

        var anims = new AnimationData[model.LogicalAnimations.Count];
        var animsByName = new Dictionary<string, int>(model.LogicalAnimations.Count);
        for (int ai = 0; ai < model.LogicalAnimations.Count; ai++)
        {
            anims[ai] = BuildAnimation(model.LogicalAnimations[ai], sharpIndex);
            animsByName[anims[ai].Name] = ai;
        }

        return new AnimatedModel(nodeCount)
        {
            Nodes = nodes,
            MeshesByNodeIndex = meshesByNode,
            Animations = anims,
            AnimationByName = animsByName,
            Min = min,
            Max = max,
        };
    }

    private static void WalkScene(SharpNode node, List<SharpNode> visited)
    {
        visited.Add(node);
        foreach (var child in node.VisualChildren)
            WalkScene(child, visited);
    }

    private static Mesh? BuildPrimitive(
        GL gl,
        MeshPrimitive primitive,
        Dictionary<int, GLTexture> textureCache,
        Matrix4x4 restWorldForAABB,
        ref Vector3 min,
        ref Vector3 max)
    {
        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions == null) return null;

        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
        var indices = primitive.GetIndices();
        if (indices == null) return null;

        var verts = new Vertex[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            var localPos = positions[i];
            verts[i] = new Vertex
            {
                Position = localPos,
                Normal = normals != null ? normals[i] : Vector3.UnitY,
                Uv = uvs != null ? uvs[i] : Vector2.Zero,
                Color = colors != null ? colors[i] : Vector4.One,
            };
            var worldPos = Vector3.Transform(localPos, restWorldForAABB);
            min = Vector3.Min(min, worldPos);
            max = Vector3.Max(max, worldPos);
        }

        var idxArr = new uint[indices.Count];
        for (int i = 0; i < indices.Count; i++)
            idxArr[i] = indices[i];

        var mesh = new Mesh(gl, verts, idxArr);
        mesh.Texture = ResolveTexture(gl, primitive.Material, textureCache);
        return mesh;
    }

    private static AnimationData BuildAnimation(Animation src, Dictionary<SharpNode, int> sharpIndex)
    {
        var translation = new Dictionary<int, SharpGLTF.Animations.ICurveSampler<Vector3>>();
        var rotation = new Dictionary<int, SharpGLTF.Animations.ICurveSampler<Quaternion>>();
        var scale = new Dictionary<int, SharpGLTF.Animations.ICurveSampler<Vector3>>();

        foreach (var c in src.Channels)
        {
            if (c.TargetNode == null) continue;
            if (!sharpIndex.TryGetValue(c.TargetNode, out int nodeIdx)) continue;

            switch (c.TargetNodePath)
            {
                case PropertyPath.translation:
                    translation[nodeIdx] = c.GetTranslationSampler().CreateCurveSampler();
                    break;
                case PropertyPath.rotation:
                    rotation[nodeIdx] = c.GetRotationSampler().CreateCurveSampler();
                    break;
                case PropertyPath.scale:
                    scale[nodeIdx] = c.GetScaleSampler().CreateCurveSampler();
                    break;
            }
        }

        return new AnimationData
        {
            Name = src.Name ?? $"anim_{src.LogicalIndex}",
            Duration = src.Duration,
            Translation = translation,
            Rotation = rotation,
            Scale = scale,
        };
    }

    private static GLTexture? ResolveTexture(
        GL gl,
        SharpGLTF.Schema2.Material? material,
        Dictionary<int, GLTexture> cache)
    {
        if (material == null) return null;
        var channel = material.FindChannel("BaseColor");
        if (channel == null) return null;

        var sharpTex = channel.Value.Texture;
        if (sharpTex != null)
        {
            int key = sharpTex.LogicalIndex;
            if (!cache.TryGetValue(key, out var cached))
            {
                var imgBytes = sharpTex.PrimaryImage.Content.Content.ToArray();
                using var ms = new MemoryStream(imgBytes);
                using var bmp = new Bitmap(ms);
                var rgba = BitmapToRgba(bmp);
                cached = new GLTexture(gl, rgba, bmp.Width, bmp.Height);
                cache[key] = cached;
            }
            return cached;
        }

        var color = channel.Value.Color;
        byte r = (byte)Math.Clamp(color.X * 255, 0, 255);
        byte g = (byte)Math.Clamp(color.Y * 255, 0, 255);
        byte b = (byte)Math.Clamp(color.Z * 255, 0, 255);
        byte a = (byte)Math.Clamp(color.W * 255, 0, 255);
        return new GLTexture(gl, new byte[] { r, g, b, a }, 1, 1);
    }

    private static unsafe byte[] BitmapToRgba(Bitmap bmp)
    {
        var data = new byte[bmp.Width * bmp.Height * 4];
        var bd = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly,
            GdiPixelFormat.Format32bppArgb);
        try
        {
            int dst = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                byte* row = (byte*)bd.Scan0 + y * bd.Stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    data[dst++] = row[x * 4 + 2];
                    data[dst++] = row[x * 4 + 1];
                    data[dst++] = row[x * 4 + 0];
                    data[dst++] = row[x * 4 + 3];
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return data;
    }
}
