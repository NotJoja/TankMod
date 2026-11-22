using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TankMod.Runtime;

/// <summary>
/// Minimal Wavefront OBJ parser. Unity cannot import models at runtime, so the models ship as OBJ text.
/// Each "o"/"g" group becomes its own <see cref="Mesh"/>. Coordinates are converted from the right-handed
/// OBJ space to Unity's left-handed space (X is mirrored and the triangle winding is flipped).
/// </summary>
internal static class ObjLoader
{
    public static Dictionary<string, Mesh> Load(string text, bool mergeGroups = false)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var groups = new Dictionary<string, Group>();

        Group? group = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var parts = line.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v":
                    positions.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                    break;
                case "vt":
                    uvs.Add(new Vector2(F(parts[1]), F(parts[2])));
                    break;
                case "vn":
                    normals.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                    break;
                case "o":
                case "g":
                    group = GetGroup(groups, mergeGroups ? "*" : parts.Length > 1 ? parts[1] : "mesh");
                    break;
                case "f":
                    group ??= GetGroup(groups, mergeGroups ? "*" : "mesh");
                    AddFace(group, parts, positions, uvs, normals);
                    break;
            }
        }

        var meshes = new Dictionary<string, Mesh>();
        foreach (var kv in groups)
        {
            meshes[kv.Key] = kv.Value.Build();
        }

        return meshes;
    }

    private static Group GetGroup(Dictionary<string, Group> groups, string name)
    {
        if (!groups.TryGetValue(name, out var g)) groups[name] = g = new Group(name);
        return g;
    }

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static void AddFace(Group group, string[] parts, List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals)
    {
        var corners = new int[parts.Length - 1];
        for (var i = 1; i < parts.Length; i++)
        {
            var idx = parts[i].Split('/');
            var v = Resolve(int.Parse(idx[0], CultureInfo.InvariantCulture), positions.Count);
            var t = idx.Length > 1 && idx[1].Length > 0 ? Resolve(int.Parse(idx[1], CultureInfo.InvariantCulture), uvs.Count) : -1;
            var n = idx.Length > 2 && idx[2].Length > 0 ? Resolve(int.Parse(idx[2], CultureInfo.InvariantCulture), normals.Count) : -1;
            corners[i - 1] = group.Vertex(v, t, n, positions, uvs, normals);
        }

        // Triangle fan; flipped winding because X is mirrored.
        for (var i = 1; i < corners.Length - 1; i++)
        {
            group.Triangles.Add(corners[0]);
            group.Triangles.Add(corners[i + 1]);
            group.Triangles.Add(corners[i]);
        }
    }

    // OBJ indices are 1-based; negative indices are relative to the end of the list.
    private static int Resolve(int index, int count) => index > 0 ? index - 1 : count + index;

    private sealed class Group
    {
        public readonly string Name;
        public readonly List<int> Triangles = new();
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Vector3> _normals = new();
        private readonly Dictionary<(int, int, int), int> _lookup = new();
        private bool _hasUv;
        private bool _hasNormals;

        public Group(string name) => Name = name;

        public int Vertex(int v, int t, int n, List<Vector3> positions, List<Vector2> uvs, List<Vector3> normals)
        {
            if (_lookup.TryGetValue((v, t, n), out var existing)) return existing;

            var p = positions[v];
            _vertices.Add(new Vector3(-p.x, p.y, p.z));

            if (t >= 0) { _hasUv = true; _uvs.Add(uvs[t]); } else _uvs.Add(Vector2.zero);
            if (n >= 0) { _hasNormals = true; var nn = normals[n]; _normals.Add(new Vector3(-nn.x, nn.y, nn.z)); }
            else _normals.Add(Vector3.up);

            var created = _vertices.Count - 1;
            _lookup[(v, t, n)] = created;
            return created;
        }

        public Mesh Build()
        {
            var mesh = new Mesh { name = Name };
            if (_vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            if (_hasUv) mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(Triangles, 0);
            if (_hasNormals) mesh.SetNormals(_normals); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
