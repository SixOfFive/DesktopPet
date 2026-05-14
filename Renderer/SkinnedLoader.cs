using System.Drawing;
using System.Numerics;
using SharpGLTF.Schema2;
using Silk.NET.OpenGL;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;
using GLTexture = Neko.Renderer.Texture;
using SharpNode = SharpGLTF.Schema2.Node;

namespace Neko.Renderer;

internal static class SkinnedLoader
{
    public static SkinnedModel Load(GL gl, string meshGlbPath, IEnumerable<string> animGlbPaths, string? texturePath)
    {
        var meshRoot = ModelRoot.Load(meshGlbPath);
        var scene = meshRoot.DefaultScene ?? meshRoot.LogicalScenes[0];

        var sharpNodes = new List<SharpNode>();
        foreach (var root in scene.VisualChildren)
            WalkScene(root, sharpNodes);

        int nodeCount = sharpNodes.Count;
        var nameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var sharpToIdx = new Dictionary<SharpNode, int>(nodeCount);
        var nodes = new NodeData[nodeCount];
        var restWorld = new Matrix4x4[nodeCount];

        for (int i = 0; i < nodeCount; i++)
        {
            var src = sharpNodes[i];
            sharpToIdx[src] = i;
            if (!string.IsNullOrEmpty(src.Name))
                nameToIndex[src.Name] = i;

            int parent = src.VisualParent != null && sharpToIdx.TryGetValue(src.VisualParent, out var p) ? p : -1;
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

        var skinnedNode = sharpNodes.FirstOrDefault(n => n.Mesh != null && n.Skin != null)
            ?? throw new InvalidOperationException($"No skinned mesh in '{meshGlbPath}'");
        var sharpSkin = skinnedNode.Skin!;
        var sharpMesh = skinnedNode.Mesh!;

        int jointCount = sharpSkin.JointsCount;
        var jointNodeIndices = new int[jointCount];
        var ibms = new Matrix4x4[jointCount];
        for (int j = 0; j < jointCount; j++)
        {
            var pair = sharpSkin.GetJoint(j);
            jointNodeIndices[j] = sharpToIdx[pair.Joint];
            ibms[j] = pair.InverseBindMatrix;
        }

        var primitive = sharpMesh.Primitives[0];
        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array()
            ?? throw new InvalidOperationException("Skinned mesh missing POSITION");
        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
        var joints = primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array()
            ?? throw new InvalidOperationException("Skinned mesh missing JOINTS_0");
        var weights = primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array()
            ?? throw new InvalidOperationException("Skinned mesh missing WEIGHTS_0");
        var indices = primitive.GetIndices()
            ?? throw new InvalidOperationException("Skinned mesh missing indices");

        var verts = new SkinnedVertex[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            verts[i] = new SkinnedVertex
            {
                Position = positions[i],
                Normal = normals != null ? normals[i] : Vector3.UnitY,
                Uv = uvs != null ? uvs[i] : Vector2.Zero,
                Color = colors != null ? colors[i] : Vector4.One,
                J0 = (int)joints[i].X, J1 = (int)joints[i].Y, J2 = (int)joints[i].Z, J3 = (int)joints[i].W,
                W0 = weights[i].X, W1 = weights[i].Y, W2 = weights[i].Z, W3 = weights[i].W,
            };
        }

        var restJointMatrices = new Matrix4x4[jointCount];
        for (int j = 0; j < jointCount; j++)
            restJointMatrices[j] = ibms[j] * restWorld[jointNodeIndices[j]];

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in verts)
        {
            var p0 = Vector3.Transform(v.Position, restJointMatrices[v.J0]) * v.W0;
            var p1 = Vector3.Transform(v.Position, restJointMatrices[v.J1]) * v.W1;
            var p2 = Vector3.Transform(v.Position, restJointMatrices[v.J2]) * v.W2;
            var p3 = Vector3.Transform(v.Position, restJointMatrices[v.J3]) * v.W3;
            var posed = p0 + p1 + p2 + p3;
            min = Vector3.Min(min, posed);
            max = Vector3.Max(max, posed);
        }

        var idxArr = new uint[indices.Count];
        for (int i = 0; i < indices.Count; i++) idxArr[i] = indices[i];

        var (vao, vbo, ebo) = UploadMesh(gl, verts, idxArr);

        var anims = new List<SkinnedAnimation>();
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var anim in meshRoot.LogicalAnimations)
            TryAddAnimation(anim, nameToIndex, sharpToIdx, anims, byName);

        foreach (var animPath in animGlbPaths)
        {
            var animRoot = ModelRoot.Load(animPath);
            foreach (var anim in animRoot.LogicalAnimations)
                TryAddAnimation(anim, nameToIndex, null, anims, byName);
        }

        GLTexture? texture = null;
        if (texturePath != null && File.Exists(texturePath))
        {
            using var bmp = new Bitmap(texturePath);
            texture = new GLTexture(gl, BitmapToRgba(bmp), bmp.Width, bmp.Height);
        }

        return new SkinnedModel(gl, nodeCount, jointCount)
        {
            Nodes = nodes,
            JointNodeIndices = jointNodeIndices,
            InverseBindMatrices = ibms,
            MeshBindShape = Matrix4x4.Identity,
            Vao = vao,
            Vbo = vbo,
            Ebo = ebo,
            IndexCount = (uint)idxArr.Length,
            BaseTexture = texture,
            Animations = anims.ToArray(),
            AnimationByName = byName,
            Min = min,
            Max = max,
        };
    }

    private static void TryAddAnimation(
        Animation src,
        Dictionary<string, int> nameToIndex,
        Dictionary<SharpNode, int>? sameRootMap,
        List<SkinnedAnimation> output,
        Dictionary<string, int> byName)
    {
        if (src.Duration <= 0.01f) return;
        string baseName = src.Name ?? $"anim_{src.LogicalIndex}";
        string cleanName = baseName.Contains('|') ? baseName[(baseName.LastIndexOf('|') + 1)..] : baseName;
        if (byName.ContainsKey(cleanName)) cleanName = baseName;

        var translation = new Dictionary<int, SharpGLTF.Animations.ICurveSampler<Vector3>>();
        var rotation = new Dictionary<int, SharpGLTF.Animations.ICurveSampler<Quaternion>>();
        var scale = new Dictionary<int, SharpGLTF.Animations.ICurveSampler<Vector3>>();

        foreach (var c in src.Channels)
        {
            if (c.TargetNode == null) continue;

            int targetIdx;
            if (sameRootMap != null && sameRootMap.TryGetValue(c.TargetNode, out var idx))
                targetIdx = idx;
            else if (!string.IsNullOrEmpty(c.TargetNode.Name)
                  && nameToIndex.TryGetValue(c.TargetNode.Name, out var byNameIdx))
                targetIdx = byNameIdx;
            else
                continue;

            switch (c.TargetNodePath)
            {
                case PropertyPath.translation:
                    translation[targetIdx] = c.GetTranslationSampler().CreateCurveSampler();
                    break;
                case PropertyPath.rotation:
                    rotation[targetIdx] = c.GetRotationSampler().CreateCurveSampler();
                    break;
                case PropertyPath.scale:
                    scale[targetIdx] = c.GetScaleSampler().CreateCurveSampler();
                    break;
            }
        }

        var data = new SkinnedAnimation
        {
            Name = cleanName,
            Duration = src.Duration,
            Translation = translation,
            Rotation = rotation,
            Scale = scale,
        };
        byName[cleanName] = output.Count;
        output.Add(data);
    }

    private static void WalkScene(SharpNode n, List<SharpNode> output)
    {
        output.Add(n);
        foreach (var c in n.VisualChildren) WalkScene(c, output);
    }

    private static unsafe (uint vao, uint vbo, uint ebo) UploadMesh(GL gl, SkinnedVertex[] verts, uint[] indices)
    {
        uint vao = gl.GenVertexArray();
        gl.BindVertexArray(vao);

        uint vbo = gl.GenBuffer();
        gl.BindBuffer(GLEnum.ArrayBuffer, vbo);
        fixed (SkinnedVertex* p = verts)
            gl.BufferData(GLEnum.ArrayBuffer, (nuint)(verts.Length * SkinnedVertex.SizeBytes), p, GLEnum.StaticDraw);

        uint ebo = gl.GenBuffer();
        gl.BindBuffer(GLEnum.ElementArrayBuffer, ebo);
        fixed (uint* p = indices)
            gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), p, GLEnum.StaticDraw);

        uint stride = SkinnedVertex.SizeBytes;
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (void*)0);   gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, GLEnum.Float, false, stride, (void*)12);  gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 2, GLEnum.Float, false, stride, (void*)24);  gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(3, 4, GLEnum.Float, false, stride, (void*)32);  gl.EnableVertexAttribArray(3);
        gl.VertexAttribIPointer(4, 4, GLEnum.Int,         stride, (void*)48);  gl.EnableVertexAttribArray(4);
        gl.VertexAttribPointer(5, 4, GLEnum.Float, false, stride, (void*)64);  gl.EnableVertexAttribArray(5);

        gl.BindVertexArray(0);
        return (vao, vbo, ebo);
    }

    private static unsafe byte[] BitmapToRgba(Bitmap bmp)
    {
        var data = new byte[bmp.Width * bmp.Height * 4];
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, GdiPixelFormat.Format32bppArgb);
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
        finally { bmp.UnlockBits(bd); }
        return data;
    }
}
