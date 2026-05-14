using System.Drawing;
using System.Numerics;
using SharpGLTF.Schema2;
using Silk.NET.OpenGL;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageLockMode = System.Drawing.Imaging.ImageLockMode;
using GLTexture = Neko.Renderer.Texture;

namespace Neko.Renderer;

internal sealed class LoadedModel
{
    public required List<Mesh> Meshes { get; init; }
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 SizeVec => Max - Min;
    public float MaxDim => Math.Max(SizeVec.X, Math.Max(SizeVec.Y, SizeVec.Z));
}

internal static class GltfLoader
{
    public static LoadedModel Load(GL gl, string path)
    {
        var model = ModelRoot.Load(path);
        var meshes = new List<Mesh>();
        var textureCache = new Dictionary<int, GLTexture>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        var scene = model.DefaultScene ?? model.LogicalScenes[0];

        foreach (var node in scene.VisualChildren)
            WalkNode(gl, node, Matrix4x4.Identity, meshes, textureCache, ref min, ref max);

        if (meshes.Count == 0)
            throw new InvalidOperationException($"No renderable meshes in '{path}'");

        return new LoadedModel { Meshes = meshes, Min = min, Max = max };
    }

    private static void WalkNode(
        GL gl,
        Node node,
        Matrix4x4 parentXform,
        List<Mesh> meshes,
        Dictionary<int, GLTexture> textureCache,
        ref Vector3 min,
        ref Vector3 max)
    {
        var worldXform = node.LocalMatrix * parentXform;

        if (node.Mesh != null)
        {
            foreach (var primitive in node.Mesh.Primitives)
                AddPrimitive(gl, primitive, worldXform, meshes, textureCache, ref min, ref max);
        }

        foreach (var child in node.VisualChildren)
            WalkNode(gl, child, worldXform, meshes, textureCache, ref min, ref max);
    }

    private static void AddPrimitive(
        GL gl,
        MeshPrimitive primitive,
        Matrix4x4 worldXform,
        List<Mesh> meshes,
        Dictionary<int, GLTexture> textureCache,
        ref Vector3 min,
        ref Vector3 max)
    {
        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions == null) return;

        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors4 = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
        var indices = primitive.GetIndices();
        if (indices == null) return;

        var verts = new Vertex[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            var pos = Vector3.Transform(positions[i], worldXform);
            min = Vector3.Min(min, pos);
            max = Vector3.Max(max, pos);
            verts[i] = new Vertex
            {
                Position = pos,
                Normal = normals != null
                    ? Vector3.Normalize(Vector3.TransformNormal(normals[i], worldXform))
                    : Vector3.UnitY,
                Uv = uvs != null ? uvs[i] : Vector2.Zero,
                Color = colors4 != null ? colors4[i] : Vector4.One,
            };
        }

        var idxArr = new uint[indices.Count];
        for (int i = 0; i < indices.Count; i++)
            idxArr[i] = indices[i];

        var mesh = new Mesh(gl, verts, idxArr);
        mesh.Texture = ResolveTexture(gl, primitive.Material, textureCache);
        meshes.Add(mesh);
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
                    data[dst++] = row[x * 4 + 2]; // R
                    data[dst++] = row[x * 4 + 1]; // G
                    data[dst++] = row[x * 4 + 0]; // B
                    data[dst++] = row[x * 4 + 3]; // A
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
