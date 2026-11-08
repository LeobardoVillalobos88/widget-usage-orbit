using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace UsageOrbit.Data
{
    internal static class Json
    {
        public static Dictionary<string, object> Parse(string text)
        {
            var value = new JavaScriptSerializer { MaxJsonLength = 2097152 }.DeserializeObject(text) as Dictionary<string, object>;
            if (value == null) throw new FormatException("Se esperaba un objeto JSON.");
            return value;
        }

        public static string Serialize(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static object Get(Dictionary<string, object> value, string key)
        {
            object result;
            return value != null && value.TryGetValue(key, out result) ? result : null;
        }
        public static Dictionary<string, object> Object(Dictionary<string, object> value, string key)
        {
            return Get(value, key) as Dictionary<string, object>;
        }
        public static double? Number(Dictionary<string, object> value, string key)
        {
            object raw = Get(value, key);
            if (!(raw is int || raw is long || raw is double || raw is decimal || raw is float)) return null;
            double number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return double.IsNaN(number) || double.IsInfinity(number) ? null : (double?)number;
        }
        public static int? Minutes(Dictionary<string, object> value, string key)
        {
            double? number = Number(value, key);
            return number.HasValue && number > 0 && number <= int.MaxValue && number == Math.Floor(number.Value)
                ? (int?)number.Value : null;
        }
        public static DateTimeOffset? Epoch(double? value, bool milliseconds)
        {
            if (!value.HasValue) return null;
            try { return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(value.Value / (milliseconds ? 1000 : 1)); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
    }
}
