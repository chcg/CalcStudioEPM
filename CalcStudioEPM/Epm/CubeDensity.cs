using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace CalcStudioEPM.Epm
{
    /// <summary>
    /// Which dimensions are dense in each cube: what Planning reports (the "density" of each
    /// dimension in plantypes/{cube}/dimensions, kept by the dimension download). For a cube
    /// downloaded before that was kept, the user's choice in the Dimension Navigator
    /// ({LocalFolder}\.epm\density.json: { "Plan1": ["Account", "Period", "View"] }), else
    /// the Account- and Period-type dimensions (Planning's standard setup).
    /// </summary>
    public static class CubeDensity
    {
        private static string FilePath(string localFolder) => Path.Combine(EpmMetadata.Folder(localFolder), "density.json");

        private static Dictionary<string, List<string>> Load(string localFolder)
        {
            try
            {
                string f = FilePath(localFolder);
                if (File.Exists(f))
                {
                    var d = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(f, Encoding.UTF8));
                    if (d != null) return new Dictionary<string, List<string>>(d, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { /* unreadable: defaults */ }
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>The default: Account- and Period-type dimensions.</summary>
        public static bool DefaultDense(EpmMetadata.DimensionInfo d) =>
            string.Equals(d?.DimType, "Account", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d?.DimType, "Period", StringComparison.OrdinalIgnoreCase);

        public static bool IsAttribute(EpmMetadata.DimensionInfo d) =>
            (d?.DimType ?? "").IndexOf("Attribute", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>True when the user chose the cube's dense dimensions (not the default).</summary>
        public static bool IsChosen(string localFolder, string cube) => Load(localFolder).ContainsKey(cube ?? "");

        /// <summary>True when Planning reported the dimension's density in this cube.</summary>
        public static bool FromEpm(EpmMetadata md, string cube, string dimension) =>
            !string.IsNullOrEmpty(md?.Dims?.Get(cube, dimension)?.Density);

        public static bool IsDense(string localFolder, EpmMetadata md, string cube, string dimension)
        {
            string reported = md?.Dims?.Get(cube, dimension)?.Density;
            if (!string.IsNullOrEmpty(reported)) return string.Equals(reported, "Dense", StringComparison.OrdinalIgnoreCase);
            var chosen = Load(localFolder);
            if (cube != null && chosen.TryGetValue(cube, out List<string> dense))
                return dense.Contains(dimension, StringComparer.OrdinalIgnoreCase);
            return DefaultDense(md?.Dims?.Get(cube, dimension));
        }

        /// <summary>Marks one dimension dense or sparse in a cube (the others keep their current state).</summary>
        public static void SetDense(string localFolder, EpmMetadata md, string cube, string dimension, bool dense)
        {
            var chosen = Load(localFolder);
            if (!chosen.TryGetValue(cube, out List<string> list))
            {
                List<string> dims = md.Dims.Cubes.TryGetValue(cube, out var l) ? l : new List<string>();
                list = dims.Where(d => DefaultDense(md.Dims.Get(cube, d))).ToList();
                chosen[cube] = list;
            }
            list.RemoveAll(d => string.Equals(d, dimension, StringComparison.OrdinalIgnoreCase));
            if (dense) list.Add(dimension);
            Directory.CreateDirectory(EpmMetadata.Folder(localFolder));
            File.WriteAllText(FilePath(localFolder), JsonConvert.SerializeObject(chosen, Formatting.Indented),
                              new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        /// <summary>
        /// The cube's dimensions for a FIX, in cube order: the sparse ones, then the dense ones.
        /// Attribute dimensions are left out (they are not fixed on directly).
        /// </summary>
        public static List<(string Name, bool Dense)> FixDimensions(string localFolder, EpmMetadata md, string cube)
        {
            var result = new List<(string, bool)>();
            if (md == null || !md.HasDimensions || cube == null || !md.Dims.Cubes.TryGetValue(cube, out List<string> dims)) return result;
            var all = dims.Where(d => !IsAttribute(md.Dims.Get(cube, d)))
                          .Select(d => (Name: d, Dense: IsDense(localFolder, md, cube, d))).ToList();
            result.AddRange(all.Where(d => !d.Dense));
            result.AddRange(all.Where(d => d.Dense));
            return result;
        }
    }
}
