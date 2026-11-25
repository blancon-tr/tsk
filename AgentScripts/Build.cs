using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class Build
{
    // ------------------------------------------------------------------ texture generation
    class Tex { public int w, h; public Color[] c; public float[] hm; public Tex(int W, int Hh) { w = W; h = Hh; c = new Color[W * Hh]; hm = new float[W * Hh]; } }

    static float H01(int x, int y, int seed)
    {
        unchecked
        {
            uint n = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(seed * 1442695041);
            n = (n ^ (n >> 13)) * 1274126177u;
            n ^= n >> 16;
            return (n & 0xFFFFFFu) / 16777216f;
        }
    }

    static float VN(float u, float v, int px, int py, int seed)
    {
        float x = u * px, y = v * py;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int xa = ((x0 % px) + px) % px, xb = (xa + 1) % px, ya = ((y0 % py) + py) % py, yb = (ya + 1) % py;
        float a = H01(xa, ya, seed), b = H01(xb, ya, seed), c = H01(xa, yb, seed), d = H01(xb, yb, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Fbm(float u, float v, int px, int py, int oct, int seed)
    {
        float s = 0, a = 0.5f, t = 0;
        for (int o = 0; o < oct; o++) { s += a * VN(u, v, px << o, py << o, seed + o * 17); t += a; a *= 0.5f; }
        return s / t;
    }

    static Tex Make(Func<int, int, float, float, (Color, float)> f)
    {
        var t = new Tex(512, 512);
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
            {
                var r = f(x, y, x / 512f, y / 512f);
                t.c[x + y * 512] = r.Item1; t.hm[x + y * 512] = r.Item2;
            }
        return t;
    }

    static Tex Brick()
    {
        return Make((x, y, u, v) =>
        {
            int row = y / 32, off = (row % 2) * 64, xs = x + off, bx = xs % 128, idx = (xs / 128) % 4, by = y % 32;
            float rnd = H01(row, idx, 3), n = Fbm(u, v, 8, 8, 4, 5), fine = H01(x, y, 9);
            if (bx < 4 || by < 4) { float g = 0.62f + 0.2f * n; return (new Color(g, g * 0.97f, g * 0.92f, 1), 0.05f * n); }
            Color b = Color.Lerp(new Color(0.5f, 0.2f, 0.14f), new Color(0.7f, 0.33f, 0.22f), rnd);
            if (H01(row, idx, 11) < 0.12f) b *= 0.72f;
            Color col = b * (0.75f + 0.5f * n) * (0.93f + 0.14f * fine); col.a = 1;
            int d = Mathf.Min(Mathf.Min(bx - 4, 127 - bx), Mathf.Min(by - 4, 31 - by));
            float bev = Mathf.Clamp01(d / 2f);
            return (col, (0.7f + 0.2f * n + 0.1f * fine) * (0.5f + 0.5f * bev));
        });
    }

    static Tex Stucco()
    {
        return Make((x, y, u, v) =>
        {
            float n = Fbm(u, v, 4, 4, 5, 21), f = H01(x, y, 22);
            float g = (0.82f + 0.18f * n) * (0.95f + 0.1f * f);
            return (new Color(g, g, g, 1), n * 0.6f + f * 0.4f);
        });
    }

    static Tex RoofTex()
    {
        return Make((x, y, u, v) =>
        {
            int row = y / 64, off = (row % 2) * 64, xs = x + off, tx = xs % 128, ty = y % 64, idx = (xs / 128) % 4;
            float rnd = H01(row, idx, 31), n = Fbm(u, v, 8, 8, 4, 32), t = ty / 64f;
            Color b = Color.Lerp(new Color(0.2f, 0.21f, 0.24f), new Color(0.34f, 0.33f, 0.36f), rnd);
            float shade = (0.8f + 0.4f * n) * (0.85f + 0.25f * (1 - t));
            if (ty >= 56) shade *= 0.55f;
            if (ty < 3) shade *= 1.25f;
            bool groove = tx < 3 || tx >= 126;
            if (groove) shade *= 0.35f;
            Color col = b * shade; col.a = 1;
            return (col, groove ? 0f : 1 - t * 0.8f);
        });
    }

    static Tex Wood()
    {
        return Make((x, y, u, v) =>
        {
            int row = y / 64, ty = y % 64;
            float rnd = H01(row, 0, 41); int jx = (int)(H01(row, 1, 42) * 512);
            float grain = Fbm(u, v, 2, 40, 5, 43), fine = H01(x, y, 44);
            Color b = Color.Lerp(new Color(0.55f, 0.37f, 0.22f), new Color(0.78f, 0.58f, 0.38f), rnd);
            Color col = b * (0.7f + 0.55f * grain) * (0.95f + 0.1f * fine); col.a = 1;
            if (ty < 2 || Mathf.Abs(x - jx) < 2) return (new Color(0.07f, 0.05f, 0.04f, 1), 0f);
            return (col, 0.6f + 0.3f * grain);
        });
    }

    static Tex Concrete()
    {
        return Make((x, y, u, v) =>
        {
            float n = Fbm(u, v, 4, 4, 5, 51), sp = H01(x, y, 52);
            float g = 0.55f + 0.25f * n;
            if (sp < 0.03f) g *= 0.75f; else if (sp > 0.985f) g *= 1.2f;
            if (x < 3 || y < 3) return (new Color(g * 0.4f, g * 0.4f, g * 0.4f, 1), 0f);
            return (new Color(g, g, g * 0.98f, 1), 0.5f + 0.3f * n + 0.2f * sp);
        });
    }

    static Tex Asphalt()
    {
        return Make((x, y, u, v) =>
        {
            float n = Fbm(u, v, 4, 4, 5, 61), sp = H01(x, y, 62);
            float g = 0.13f + 0.07f * n;
            if (sp > 0.97f) g += 0.25f * H01(x, y, 63);
            return (new Color(g, g, g * 1.03f, 1), n * 0.4f + sp * 0.6f);
        });
    }

    static Tex Grass()
    {
        return Make((x, y, u, v) =>
        {
            float n = Fbm(u, v, 4, 4, 4, 71), f = Fbm(u, v, 64, 64, 3, 72), fine = H01(x, y, 73);
            Color c = Color.Lerp(new Color(0.12f, 0.28f, 0.07f), new Color(0.33f, 0.55f, 0.17f), n * 0.55f + f * 0.45f) * (0.85f + 0.3f * fine);
            c.a = 1;
            return (c, f * 0.7f + fine * 0.3f);
        });
    }

    static Tex Stone()
    {
        const int N = 7;
        return Make((x, y, u, v) =>
        {
            float gx = u * N, gy = v * N; int ci = Mathf.FloorToInt(gx), cj = Mathf.FloorToInt(gy);
            float f1 = 9, f2 = 9; int bi = 0, bj = 0;
            for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ni = ci + di, nj = cj + dj; int wi = ((ni % N) + N) % N, wj = ((nj % N) + N) % N;
                    float px = ni + H01(wi, wj, 81) * 0.8f + 0.1f, py = nj + H01(wi, wj, 82) * 0.8f + 0.1f;
                    float d = Mathf.Sqrt((gx - px) * (gx - px) + (gy - py) * (gy - py));
                    if (d < f1) { f2 = f1; f1 = d; bi = wi; bj = wj; } else if (d < f2) f2 = d;
                }
            float edge = f2 - f1, tone = Mathf.Lerp(0.4f, 0.62f, H01(bi, bj, 83)), n = Fbm(u, v, 16, 16, 4, 84);
            if (edge < 0.07f) return (new Color(0.22f, 0.21f, 0.2f, 1), 0f);
            float g = tone * (0.8f + 0.4f * n);
            return (new Color(g, g * 0.97f, g * 0.92f, 1), Mathf.Clamp01(edge * 3) * 0.7f + 0.3f * n);
        });
    }

    static Tex GDoor()
    {
        return Make((x, y, u, v) =>
        {
            float br = Fbm(u, v, 2, 128, 3, 91); float g = 0.85f * (0.94f + 0.1f * br);
            if (x < 5 || x > 506 || y < 5 || y > 506) return (new Color(0.45f, 0.45f, 0.47f, 1), 0f);
            int dd = Mathf.Min(Mathf.Min(x - 36, 476 - x), Mathf.Min(y - 56, 456 - y));
            if (dd >= 0) { float k = dd < 6 ? 0.85f : 1f; return (new Color(g * k, g * k, g * k * 1.02f, 1), 0.55f + 0.4f * Mathf.Clamp01(dd / 14f)); }
            return (new Color(g, g, g * 1.02f, 1), 0.5f);
        });
    }

    static Tex Bark()
    {
        return Make((x, y, u, v) =>
        {
            float n = Fbm(u, v, 12, 3, 5, 101);
            Color c = Color.Lerp(new Color(0.16f, 0.1f, 0.07f), new Color(0.38f, 0.28f, 0.2f), n);
            if (n < 0.35f) c *= 0.6f;
            c.a = 1;
            return (c, n);
        });
    }

    static Tex Leaves()
    {
        return Make((x, y, u, v) =>
        {
            float n = Fbm(u, v, 16, 16, 4, 111), b = H01(x, y, 112);
            Color c = Color.Lerp(new Color(0.07f, 0.22f, 0.05f), new Color(0.26f, 0.5f, 0.14f), n) * (0.8f + 0.4f * b);
            c.a = 1;
            return (c, n);
        });
    }

    static void SaveTex(string name, Tex t, float ns)
    {
        string dir = Application.dataPath + "/Textures"; Directory.CreateDirectory(dir);
        var tex = new Texture2D(t.w, t.h, TextureFormat.RGBA32, false);
        tex.SetPixels(t.c); tex.Apply();
        File.WriteAllBytes(dir + "/" + name + ".png", tex.EncodeToPNG());
        var nc = new Color[t.w * t.h];
        for (int y = 0; y < t.h; y++)
            for (int x = 0; x < t.w; x++)
            {
                float l = t.hm[((x - 1 + t.w) % t.w) + y * t.w], r = t.hm[((x + 1) % t.w) + y * t.w];
                float d = t.hm[x + ((y - 1 + t.h) % t.h) * t.w], up = t.hm[x + ((y + 1) % t.h) * t.w];
                Vector3 n = new Vector3(-(r - l) * ns, -(up - d) * ns, 1).normalized;
                nc[x + y * t.w] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
            }
        tex.SetPixels(nc); tex.Apply();
        File.WriteAllBytes(dir + "/" + name + "_n.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        ImportTex("Assets/Textures/" + name + ".png", false);
        ImportTex("Assets/Textures/" + name + "_n.png", true);
    }

    static void ImportTex(string p, bool normal)
    {
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);
        var ti = AssetImporter.GetAtPath(p) as TextureImporter;
        if (ti == null) return;
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.anisoLevel = 8;
        ti.SaveAndReimport();
    }

    static void EnsureTex(string name, Func<Tex> gen, float ns)
    {
        if (File.Exists(Application.dataPath + "/Textures/" + name + ".png") && File.Exists(Application.dataPath + "/Textures/" + name + "_n.png")) return;
        SaveTex(name, gen(), ns);
    }

    // ------------------------------------------------------------------ materials
    static Material brick, stucco, paint, roof, woodFloor, woodDoor, concrete, asphalt, grass, stone, gdoorM, bark, leaves, glass, metal;

    static Material Mat(string name, string tex, Color tint, float smooth, float bump = 1f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/" + tex + ".png"));
        m.SetColor("_BaseColor", tint);
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/" + tex + "_n.png"));
        m.SetFloat("_BumpScale", bump);
        m.EnableKeyword("_NORMALMAP");
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(m, "Assets/Materials/" + name + ".mat");
        return m;
    }

    // ------------------------------------------------------------------ geometry helpers
    static Vector2 UVOf(Vector3 p, Vector3 n, Vector2 tile)
    {
        Vector3 a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        Vector2 uv;
        if (a.x >= a.y && a.x >= a.z) uv = new Vector2(p.z, p.y);
        else if (a.y >= a.z) uv = new Vector2(p.x, p.z);
        else uv = new Vector2(p.x, p.y);
        return new Vector2(uv.x / tile.x, uv.y / tile.y);
    }

    class MB
    {
        public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int> t = new List<int>();
        public void Face(Transform tr, bool wuv, Vector2 tile, Vector3[] c, Vector3 nl)
        {
            int s = v.Count; Vector3 nw = wuv ? tr.TransformDirection(nl) : nl;
            foreach (var p in c) { v.Add(p); n.Add(nl); Vector3 pw = wuv ? tr.TransformPoint(p) : p; uv.Add(UVOf(pw, nw, tile)); }
            bool flip = Vector3.Dot(Vector3.Cross(c[1] - c[0], c[2] - c[0]), nl) > 0;
            if (c.Length == 3)
            {
                if (flip) { t.Add(s); t.Add(s + 2); t.Add(s + 1); } else { t.Add(s); t.Add(s + 1); t.Add(s + 2); }
            }
            else
            {
                if (flip) t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                else t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
        }
    }

    static Mesh Finish(GameObject g, MB mb, Material m)
    {
        var mesh = new Mesh(); mesh.name = g.name;
        mesh.SetVertices(mb.v); mesh.SetNormals(mb.n); mesh.SetUVs(0, mb.uv); mesh.SetTriangles(mb.t, 0);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
        return mesh;
    }

    static GameObject Box(string name, Transform parent, Vector3 lp, Vector3 size, Material m, float tile, bool col = true, bool wuv = true, Quaternion? rot = null)
    { return Box(name, parent, lp, size, m, new Vector2(tile, tile), col, wuv, rot); }

    static GameObject Box(string name, Transform parent, Vector3 lp, Vector3 size, Material m, Vector2 tile, bool col = true, bool wuv = true, Quaternion? rot = null)
    {
        var g = new GameObject(name); var tr = g.transform;
        tr.SetParent(parent, false); tr.localPosition = lp; tr.localRotation = rot ?? Quaternion.identity;
        var mb = new MB(); Vector3 h = size / 2;
        for (int a = 0; a < 3; a++)
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 n = Vector3.zero; n[a] = s;
                int b = (a + 1) % 3, c = (a + 2) % 3;
                Vector3 eb = Vector3.zero, ec = Vector3.zero; eb[b] = h[b]; ec[c] = h[c];
                Vector3 ctr = n; ctr[a] = s * h[a];
                mb.Face(tr, wuv, tile, new[] { ctr - eb - ec, ctr + eb - ec, ctr + eb + ec, ctr - eb + ec }, n);
            }
        Finish(g, mb, m);
        if (col) { var bc = g.AddComponent<BoxCollider>(); bc.center = Vector3.zero; bc.size = size; }
        return g;
    }

    static GameObject Prism(string name, Transform parent, Vector3 lp, float span, float rise, float thick, Material m, Vector2 tile)
    {
        var g = new GameObject(name); var tr = g.transform;
        tr.SetParent(parent, false); tr.localPosition = lp;
        float hx = thick / 2, hs = span / 2;
        Vector3 a0 = new Vector3(-hx, 0, -hs), a1 = new Vector3(-hx, 0, hs), a2 = new Vector3(-hx, rise, 0);
        Vector3 b0 = new Vector3(hx, 0, -hs), b1 = new Vector3(hx, 0, hs), b2 = new Vector3(hx, rise, 0);
        var mb = new MB();
        mb.Face(tr, true, tile, new[] { a0, a1, a2 }, Vector3.left);
        mb.Face(tr, true, tile, new[] { b0, b1, b2 }, Vector3.right);
        mb.Face(tr, true, tile, new[] { a0, a1, b1, b0 }, Vector3.down);
        mb.Face(tr, true, tile, new[] { a1, a2, b2, b1 }, new Vector3(0, rise, hs).normalized);
        mb.Face(tr, true, tile, new[] { a0, a2, b2, b0 }, new Vector3(0, rise, -hs).normalized);
        var mesh = Finish(g, mb, m);
        var bc = g.AddComponent<BoxCollider>(); bc.center = mesh.bounds.center; bc.size = mesh.bounds.size;
        return g;
    }

    static void Roof(Transform parent, string name, Vector3 pos, float yaw, float ridgeLen, float span, float rise, float ovhX, float ovhZ, Material roofM, Material gableM)
    {
        var R = new GameObject(name).transform; R.SetParent(parent, false); R.localPosition = pos; R.localRotation = Quaternion.Euler(0, yaw, 0);
        float hs = span / 2, th = 0.25f;
        float ang = Mathf.Atan2(rise, hs), cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
        Vector2 E = new Vector2(hs + ovhZ, -ovhZ * rise / hs), Rg = new Vector2(0, rise);
        Vector2 mid = (E + Rg) / 2; float len = (Rg - E).magnitude + 0.05f;
        foreach (int sg in new[] { 1, -1 })
        {
            Vector3 c = new Vector3(0, mid.y + cs * th / 2, sg * (mid.x + sn * th / 2));
            Box("Cati Yuzeyi", R, c, new Vector3(ridgeLen + 2 * ovhX, th, len), roofM, 1.5f, true, false, Quaternion.Euler(sg * ang * Mathf.Rad2Deg, 0, 0));
        }
        Box("Mahya", R, new Vector3(0, rise + 0.05f, 0), new Vector3(ridgeLen + 2 * ovhX, 0.2f, 0.2f), roofM, 1f, true, false, Quaternion.Euler(45, 0, 0));
        foreach (int sx in new[] { 1, -1 })
            Prism("Alin", R, new Vector3(sx * (ridgeLen / 2 - 0.15f), 0, 0), span, rise, 0.3f, gableM, new Vector2(2, 2));
    }

    class Op { public float cx, w, sill, h; public int kind; }
    static Op O(float cx, float w, float sill, float h, int kind) { return new Op { cx = cx, w = w, sill = sill, h = h, kind = kind }; }

    static void Wall(Transform parent, string name, Vector3 pos, float yaw, float L, float H, float T, Material m, float tile, params Op[] ops)
    {
        var W = new GameObject(name).transform; W.SetParent(parent, false); W.localPosition = pos; W.localRotation = Quaternion.Euler(0, yaw, 0);
        var sorted = ops.OrderBy(o => o.cx).ToList();
        float cur = -L / 2;
        Action<float, float, float, float> seg = (a, b, y0, y1) => Box("Duvar", W, new Vector3((a + b) / 2, (y0 + y1) / 2, 0), new Vector3(b - a, y1 - y0, T), m, tile);
        foreach (var o in sorted)
        {
            float l = o.cx - o.w / 2, r = o.cx + o.w / 2;
            if (l > cur + 0.001f) seg(cur, l, 0, H);
            if (o.sill > 0.001f) seg(l, r, 0, o.sill);
            if (o.sill + o.h < H - 0.001f) seg(l, r, o.sill + o.h, H);
            cur = r;
        }
        if (cur < L / 2 - 0.001f) seg(cur, L / 2, 0, H);
        foreach (var o in sorted) Fill(W, o, T);
    }

    static void Fill(Transform W, Op o, float T)
    {
        float cx = o.cx, w = o.w, h = o.h, s = o.sill;
        if (o.kind == 0) // pencere
        {
            float t = 0.07f, d = 0.16f;
            Box("Cerceve", W, new Vector3(cx, s + t / 2, 0), new Vector3(w, t, d), paint, 1f, false);
            Box("Cerceve", W, new Vector3(cx, s + h - t / 2, 0), new Vector3(w, t, d), paint, 1f, false);
            Box("Cerceve", W, new Vector3(cx - w / 2 + t / 2, s + h / 2, 0), new Vector3(t, h, d), paint, 1f, false);
            Box("Cerceve", W, new Vector3(cx + w / 2 - t / 2, s + h / 2, 0), new Vector3(t, h, d), paint, 1f, false);
            Box("Orta Cita", W, new Vector3(cx, s + h / 2, 0), new Vector3(0.05f, h - 2 * t, 0.1f), paint, 1f, false);
            if (h > 1.2f) Box("Orta Cita", W, new Vector3(cx, s + h / 2, 0), new Vector3(w - 2 * t, 0.05f, 0.1f), paint, 1f, false);
            Box("Cam", W, new Vector3(cx, s + h / 2, 0), new Vector3(w - 2 * t, h - 2 * t, 0.02f), glass, 1f, false);
            Box("Denizlik", W, new Vector3(cx, s - 0.01f, -T / 2 - 0.07f), new Vector3(w + 0.25f, 0.08f, 0.3f), paint, 1f, false);
        }
        else if (o.kind == 1) // kapi
        {
            float t = 0.08f;
            Box("Kapi Kasa", W, new Vector3(cx - w / 2 + t / 2, h / 2, 0), new Vector3(t, h, 0.18f), paint, 1f, false);
            Box("Kapi Kasa", W, new Vector3(cx + w / 2 - t / 2, h / 2, 0), new Vector3(t, h, 0.18f), paint, 1f, false);
            Box("Kapi Kasa", W, new Vector3(cx, h - t / 2, 0), new Vector3(w, t, 0.18f), paint, 1f, false);
            Box("Kapi", W, new Vector3(cx, (h - t) / 2, 0.03f), new Vector3(w - 2 * t, h - t, 0.06f), woodDoor, 1.5f, false, false);
            float pw = (w - 2 * t) / 2 - 0.15f;
            foreach (float rx in new[] { -pw / 2 - 0.02f, pw / 2 + 0.02f })
            {
                Box("Kapi Panel", W, new Vector3(cx + rx, h * 0.25f, -0.01f), new Vector3(pw, h * 0.32f, 0.02f), woodDoor, 1f, false, false);
                Box("Kapi Panel", W, new Vector3(cx + rx, h * 0.7f, -0.01f), new Vector3(pw, h * 0.34f, 0.02f), woodDoor, 1f, false, false);
            }
            Box("Kol", W, new Vector3(cx + w / 2 - 0.2f, 1.05f, -0.05f), new Vector3(0.14f, 0.03f, 0.04f), metal, 1f, false);
        }
        else // garaj kapisi
        {
            float t = 0.1f;
            Box("Kasa", W, new Vector3(cx - w / 2 + t / 2, h / 2, 0), new Vector3(t, h, 0.18f), paint, 1f, false);
            Box("Kasa", W, new Vector3(cx + w / 2 - t / 2, h / 2, 0), new Vector3(t, h, 0.18f), paint, 1f, false);
            Box("Kasa", W, new Vector3(cx, h - t / 2, 0), new Vector3(w, t, 0.18f), paint, 1f, false);
            float lw = w - 2 * t, lh = h - t;
            Box("Garaj Kapisi", W, new Vector3(cx, lh / 2, 0), new Vector3(lw, lh, 0.08f), gdoorM, new Vector2(lw / 4, lh / 4), true, false);
            for (int i = 0; i < 4; i++)
                Box("Kapi Cam", W, new Vector3(cx - lw * 3 / 8 + i * lw / 4, lh - 0.33f, -0.045f), new Vector3(lw / 4 - 0.45f, 0.26f, 0.02f), glass, 1f, false);
        }
    }

    static void Lamp(Transform p, Vector3 pos)
    {
        var g = new GameObject("Ic Isik"); g.transform.SetParent(p, false); g.transform.localPosition = pos;
        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.85f, 0.65f); l.intensity = 1.5f; l.range = 9f; l.shadows = LightShadows.None;
    }

    static void Tree(Transform p, Vector3 pos, float s)
    {
        var tr = new GameObject("Agac").transform; tr.SetParent(p, false); tr.position = pos;
        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); trunk.name = "Govde"; trunk.transform.SetParent(tr, false);
        trunk.transform.localPosition = new Vector3(0, 2.2f * s, 0); trunk.transform.localScale = new Vector3(0.45f * s, 2.2f * s, 0.45f * s);
        trunk.GetComponent<Renderer>().sharedMaterial = bark;
        Vector3[] offs = { new Vector3(0, 5.2f, 0), new Vector3(1.0f, 4.4f, 0.5f), new Vector3(-1.0f, 4.6f, -0.4f), new Vector3(0.2f, 6.3f, 0.2f) };
        float[] sz = { 4.2f, 3.2f, 3.4f, 3.0f };
        for (int i = 0; i < 4; i++)
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Sphere); f.name = "Yaprak"; f.transform.SetParent(tr, false);
            f.transform.localPosition = offs[i] * s; f.transform.localScale = new Vector3(sz[i], sz[i] * 0.85f, sz[i]) * s;
            f.GetComponent<Renderer>().sharedMaterial = leaves; Object.DestroyImmediate(f.GetComponent<Collider>());
        }
    }

    static void Bush(Transform p, Vector3 pos, float s)
    {
        var f = GameObject.CreatePrimitive(PrimitiveType.Sphere); f.name = "Calı"; f.transform.SetParent(p, false);
        f.transform.position = pos; f.transform.localScale = new Vector3(1.4f, 1.0f, 1.4f) * s;
        f.GetComponent<Renderer>().sharedMaterial = leaves; Object.DestroyImmediate(f.GetComponent<Collider>());
    }

    // ------------------------------------------------------------------ main
    public static string All()
    {
        foreach (var n in new[] { "Ev ve Garaj", "Zemin" }) { var o = GameObject.Find(n); if (o != null) Object.DestroyImmediate(o); }
        AssetDatabase.DeleteAsset("Assets/Materials");
        AssetDatabase.CreateFolder("Assets", "Materials");

        EnsureTex("tugla", Brick, 2.0f);
        EnsureTex("siva", Stucco, 2.5f);
        EnsureTex("kiremit", RoofTex, 2.0f);
        EnsureTex("ahsap", Wood, 2.0f);
        EnsureTex("beton", Concrete, 2.0f);
        EnsureTex("asfalt", Asphalt, 2.0f);
        EnsureTex("cim", Grass, 1.5f);
        EnsureTex("tas", Stone, 2.5f);
        EnsureTex("garajkapi", GDoor, 2.0f);
        EnsureTex("govde", Bark, 2.0f);
        EnsureTex("yaprak", Leaves, 1.5f);
        AssetDatabase.Refresh();

        brick = Mat("Tugla", "tugla", Color.white, 0.12f, 1.2f);
        stucco = Mat("SivaKrem", "siva", new Color(0.93f, 0.88f, 0.76f), 0.1f, 1f);
        paint = Mat("BeyazBoya", "siva", new Color(0.95f, 0.95f, 0.93f), 0.3f, 0.6f);
        roof = Mat("Kiremit", "kiremit", Color.white, 0.25f, 1.3f);
        woodFloor = Mat("AhsapZemin", "ahsap", Color.white, 0.45f, 0.8f);
        woodDoor = Mat("AhsapKapi", "ahsap", new Color(0.5f, 0.32f, 0.2f), 0.4f, 0.8f);
        concrete = Mat("Beton", "beton", Color.white, 0.1f, 0.8f);
        asphalt = Mat("Asfalt", "asfalt", Color.white, 0.2f, 1f);
        grass = Mat("Cim", "cim", Color.white, 0.05f, 1f);
        stone = Mat("Tas", "tas", Color.white, 0.15f, 1.2f);
        gdoorM = Mat("GarajKapisi", "garajkapi", new Color(0.9f, 0.9f, 0.92f), 0.4f, 1f);
        bark = Mat("Govde", "govde", Color.white, 0.05f, 1f); bark.mainTextureScale = new Vector2(3, 3);
        leaves = Mat("Yaprak", "yaprak", Color.white, 0.1f, 1f); leaves.mainTextureScale = new Vector2(4, 4);

        glass = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        glass.SetColor("_BaseColor", new Color(0.55f, 0.72f, 0.82f, 0.22f));
        glass.SetFloat("_Surface", 1); glass.SetFloat("_Blend", 0);
        glass.SetFloat("_SrcBlend", 5); glass.SetFloat("_DstBlend", 10); glass.SetFloat("_SrcBlendAlpha", 1); glass.SetFloat("_DstBlendAlpha", 10);
        glass.SetFloat("_ZWrite", 0); glass.SetFloat("_Smoothness", 0.95f); glass.SetFloat("_Metallic", 0f);
        glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.renderQueue = 3000; glass.SetOverrideTag("RenderType", "Transparent");
        AssetDatabase.CreateAsset(glass, "Assets/Materials/Cam.mat");
        metal = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        metal.SetColor("_BaseColor", new Color(0.75f, 0.68f, 0.45f)); metal.SetFloat("_Metallic", 1f); metal.SetFloat("_Smoothness", 0.8f);
        AssetDatabase.CreateAsset(metal, "Assets/Materials/Metal.mat");

        var root = new GameObject("Ev ve Garaj").transform;

        // ---------------- Cevre
        var E = new GameObject("Cevre").transform; E.SetParent(root, false);
        Box("Zemin", E, new Vector3(0, -0.2f, 20), new Vector3(160, 0.4f, 160), grass, 3f);
        Box("Yol", E, new Vector3(0, 0, -1), new Vector3(160, 0.04f, 6), asphalt, 3f);
        Box("Kaldirim", E, new Vector3(0, 0.06f, 3), new Vector3(160, 0.12f, 2), concrete, 2f);
        Box("Garaj Yolu", E, new Vector3(21, 0.06f, 7), new Vector3(5.2f, 0.12f, 6), concrete, 2f);
        Box("Ev Yolu", E, new Vector3(0, 0.05f, 5.25f), new Vector3(1.6f, 0.1f, 2.5f), concrete, 2f);
        Tree(E, new Vector3(-15, 0, 8), 1.1f); Tree(E, new Vector3(-16, 0, 22), 1.3f); Tree(E, new Vector3(13.5f, 0, 8.5f), 1.0f);
        Tree(E, new Vector3(31, 0, 11), 1.2f); Tree(E, new Vector3(14, 0, 27), 1.1f);
        foreach (float bx in new[] { -7.5f, -5.3f, 5.3f, 7.5f }) Bush(E, new Vector3(bx, 0.45f, 9.2f), 1f);
        Bush(E, new Vector3(16.5f, 0.45f, 9.3f), 1f); Bush(E, new Vector3(26f, 0.45f, 9.3f), 1f);

        // ---------------- EV (merkez 0,0,15.5)
        var H = new GameObject("Ev").transform; H.SetParent(root, false); H.localPosition = new Vector3(0, 0, 15.5f);
        Box("Temel", H, new Vector3(0, 0.25f, 0), new Vector3(18.4f, 0.5f, 11.4f), stone, 2f);
        Box("Zemin Kat Doseme", H, new Vector3(0, 0.5f, 0), new Vector3(17.4f, 0.02f, 10.4f), woodFloor, 2f, false);

        Wall(H, "On Duvar Alt", new Vector3(0, 0.5f, -5.35f), 0, 18, 3.0f, 0.3f, brick, 1f,
            O(0, 1.4f, 0, 2.3f, 1), O(-6.5f, 1.6f, 0.9f, 1.5f, 0), O(-3.2f, 1.6f, 0.9f, 1.5f, 0), O(3.2f, 1.6f, 0.9f, 1.5f, 0), O(6.5f, 1.6f, 0.9f, 1.5f, 0));
        Wall(H, "Arka Duvar Alt", new Vector3(0, 0.5f, 5.35f), 180, 18, 3.0f, 0.3f, brick, 1f,
            O(0, 1.2f, 0, 2.3f, 1), O(-6, 1.6f, 0.9f, 1.5f, 0), O(-3, 1.6f, 0.9f, 1.5f, 0), O(3, 1.6f, 0.9f, 1.5f, 0), O(6, 1.6f, 0.9f, 1.5f, 0));
        Wall(H, "Sol Duvar Alt", new Vector3(-8.85f, 0.5f, 0), 90, 10.4f, 3.0f, 0.3f, brick, 1f, O(-2.5f, 1.4f, 0.9f, 1.5f, 0), O(2.5f, 1.4f, 0.9f, 1.5f, 0));
        Wall(H, "Sag Duvar Alt", new Vector3(8.85f, 0.5f, 0), 270, 10.4f, 3.0f, 0.3f, brick, 1f, O(-2.5f, 1.4f, 0.9f, 1.5f, 0), O(2.5f, 1.4f, 0.9f, 1.5f, 0));

        Box("Kat Arasi", H, new Vector3(0, 3.65f, 0), new Vector3(18.2f, 0.3f, 11.2f), paint, 2f);
        Box("Ust Kat Doseme", H, new Vector3(0, 3.81f, 0), new Vector3(17.4f, 0.02f, 10.4f), woodFloor, 2f, false);

        Wall(H, "On Duvar Ust", new Vector3(0, 3.8f, -5.35f), 0, 18, 2.8f, 0.3f, stucco, 2f,
            O(-6.5f, 1.6f, 0.8f, 1.5f, 0), O(-3.2f, 1.6f, 0.8f, 1.5f, 0), O(0, 1.6f, 0.8f, 1.5f, 0), O(3.2f, 1.6f, 0.8f, 1.5f, 0), O(6.5f, 1.6f, 0.8f, 1.5f, 0));
        Wall(H, "Arka Duvar Ust", new Vector3(0, 3.8f, 5.35f), 180, 18, 2.8f, 0.3f, stucco, 2f,
            O(-6, 1.6f, 0.8f, 1.5f, 0), O(-3, 1.6f, 0.8f, 1.5f, 0), O(0, 1.6f, 0.8f, 1.5f, 0), O(3, 1.6f, 0.8f, 1.5f, 0), O(6, 1.6f, 0.8f, 1.5f, 0));
        Wall(H, "Sol Duvar Ust", new Vector3(-8.85f, 3.8f, 0), 90, 10.4f, 2.8f, 0.3f, stucco, 2f, O(-2.5f, 1.4f, 0.8f, 1.5f, 0), O(2.5f, 1.4f, 0.8f, 1.5f, 0));
        Wall(H, "Sag Duvar Ust", new Vector3(8.85f, 3.8f, 0), 270, 10.4f, 2.8f, 0.3f, stucco, 2f, O(-2.5f, 1.4f, 0.8f, 1.5f, 0), O(2.5f, 1.4f, 0.8f, 1.5f, 0));

        Box("Tavan", H, new Vector3(0, 6.55f, 0), new Vector3(17.4f, 0.1f, 10.4f), paint, 2f, false);
        Roof(H, "Cati", new Vector3(0, 6.6f, 0), 0, 18, 11, 3.0f, 0.5f, 0.8f, roof, stucco);

        // sundurma
        Box("Sundurma Zemin", H, new Vector3(0, 0.25f, -6.85f), new Vector3(5, 0.5f, 2.3f), concrete, 2f);
        Box("Basamak 2", H, new Vector3(0, 0.165f, -8.25f), new Vector3(3, 0.33f, 0.5f), concrete, 2f);
        Box("Basamak 1", H, new Vector3(0, 0.085f, -8.75f), new Vector3(3.4f, 0.17f, 0.5f), concrete, 2f);
        foreach (float sx in new[] { -2.2f, 2.2f }) Box("Sutun", H, new Vector3(sx, 1.925f, -7.7f), new Vector3(0.25f, 2.85f, 0.25f), paint, 2f);
        Box("Sundurma Cati", H, new Vector3(0, 3.425f, -6.9f), new Vector3(5.4f, 0.15f, 2.9f), roof, 1.5f);

        // baca
        Box("Baca", H, new Vector3(6, 9.0f, 2), new Vector3(1.1f, 4.8f, 1.1f), brick, 1f);
        Box("Baca Ust", H, new Vector3(6, 11.46f, 2), new Vector3(1.4f, 0.12f, 1.4f), concrete, 2f);

        Lamp(H, new Vector3(-4, 2.3f, 0)); Lamp(H, new Vector3(4, 2.3f, 0)); Lamp(H, new Vector3(-4, 5.5f, 0)); Lamp(H, new Vector3(4, 5.5f, 0));

        // ---------------- GARAJ (merkez 21,0,13.5)
        var G = new GameObject("Garaj").transform; G.SetParent(root, false); G.localPosition = new Vector3(21, 0, 13.5f);
        Box("Garaj Zemin", G, new Vector3(0, 0.075f, 0), new Vector3(8.4f, 0.15f, 7.4f), concrete, 2f);
        Wall(G, "Garaj On Duvar", new Vector3(0, 0.15f, -3.35f), 0, 8, 3.2f, 0.3f, brick, 1f, O(0, 5f, 0, 2.5f, 2));
        Wall(G, "Garaj Arka Duvar", new Vector3(0, 0.15f, 3.35f), 180, 8, 3.2f, 0.3f, brick, 1f, O(0, 1.4f, 1.2f, 0.9f, 0));
        Wall(G, "Garaj Sol Duvar", new Vector3(-3.85f, 0.15f, 0), 90, 6.4f, 3.2f, 0.3f, brick, 1f, O(0, 1.4f, 1.1f, 1.0f, 0));
        Wall(G, "Garaj Sag Duvar", new Vector3(3.85f, 0.15f, 0), 270, 6.4f, 3.2f, 0.3f, brick, 1f, O(-1.5f, 0.95f, 0, 2.1f, 1));
        Box("Garaj Tavan", G, new Vector3(0, 3.3f, 0), new Vector3(7.4f, 0.1f, 6.4f), paint, 2f, false);
        Roof(G, "Garaj Cati", new Vector3(0, 3.35f, 0), 90, 7, 8, 1.7f, 0.45f, 0.6f, roof, stucco);
        Lamp(G, new Vector3(0, 2.8f, 0));

        EditorUtility.SetDirty(root.gameObject);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        try
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { sv.LookAt(new Vector3(10, 3.5f, 14), Quaternion.Euler(16, -28, 0), 34f); sv.Repaint(); }
        }
        catch { }
        return "ok";
    }
}
