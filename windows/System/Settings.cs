using System;
using System.IO;
using System.Text;
using UsageOrbit.Data;
using UsageOrbit.UI;

namespace UsageOrbit.Platform
{
    public sealed class Settings
    {
        public int? X { get; set; }
        public int? Y { get; set; }
        public Variant Variant { get; set; }
        public ViewMode View { get; set; }
        public Assistants Show { get; set; }
        // Si es verdadero, la apariencia elegida al instalar no se puede cambiar desde el widget.
        public bool FixedVariant { get; set; }
        public string DockSide { get; set; }
        public int? DockY { get; set; }
        public static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageOrbit"); } }

        public static Settings Load()
        {
            try
            {
                var data = Json.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "settings.json")));
                var result = new Settings();
                result.X = Coordinate(data, "X");
                result.Y = Coordinate(data, "Y");
                result.DockY = Coordinate(data, "DockY");
                result.Variant = Json.Number(data, "Variant") == 1 ? Variant.Mascots : Variant.Logos;
                double? view = Json.Number(data, "View");
                result.View = view == 1 ? ViewMode.ImagesOnly : ViewMode.Full;
                double? show = Json.Number(data, "Show");
                result.Show = show == 1 ? Assistants.Claude : show == 2 ? Assistants.Codex : Assistants.Both;
                result.FixedVariant = object.Equals(Json.Get(data, "FixedVariant"), true);
                string side = Json.Get(data, "DockSide") as string;
                result.DockSide = side == "left" || side == "right" ? side : null;
                return result;
            }
            catch (Exception) { return new Settings(); }
        }
        private static int? Coordinate(System.Collections.Generic.Dictionary<string, object> data, string key)
        {
            double? value = Json.Number(data, key);
            return value >= -1000000 && value <= 1000000 ? (int?)value.Value : null;
        }
        public void Save()
        {
            string temporary = null;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "settings.json");
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, Json.Serialize(this), Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception) { Log("No se pudo guardar la configuración."); }
            finally { if (temporary != null && File.Exists(temporary)) { try { File.Delete(temporary); } catch (IOException) { } } }
        }
        public static void Log(string safeMessage)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 131072) File.WriteAllText(path, "");
                File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + safeMessage + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
