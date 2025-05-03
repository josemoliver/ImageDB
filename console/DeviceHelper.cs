using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    public static class DeviceHelper
    {
        private static readonly Dictionary<string, string> CameraMakerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "CANON", "Canon" },
        { "NIKON", "Nikon" },
        { "SONY", "Sony" },
        { "FUJIFILM", "Fujifilm" },
        { "PANASONIC", "Panasonic" },
        { "OLYMPUS", "Olympus" },
        { "OM SYSTEM", "OM System" },
        { "LEICA", "Leica" },
        { "PENTAX", "Pentax" },
        { "RICOH", "Ricoh" },
        { "KODAK", "Kodak" },
        { "CASIO", "Casio" },
        { "SAMSUNG", "Samsung" },
        { "SIGMA", "Sigma" },
        { "HASSELBLAD", "Hasselblad" },
        { "GOPRO", "GoPro" },
        { "DJI", "DJI" },
        { "PHASE ONE", "Phase One" },
        { "APPLE", "Apple" },
        { "GOOGLE", "Google" },
        { "HUAWEI", "Huawei" },
        { "XIAOMI", "Xiaomi" },
        { "ONEPLUS", "OnePlus" },
        { "BLACKMAGIC DESIGN", "Blackmagic Design" },
        { "RED DIGITAL CINEMA", "RED Digital Cinema" },
        { "SHARP", "Sharp" },
        { "VIVITAR", "Vivitar" },
        { "YASHICA", "Yashica" },
        { "BELL & HOWELL", "Bell & Howell" },
        { "TAMRON", "Tamron" },
        { "TOKINA", "Tokina" },
        { "HOLGA", "Holga" },
        { "POLAROID", "Polaroid" },
        { "LOMOGRAPHY", "Lomography" },
        { "MEIKE", "Meike" },
        { "SJCAM", "SJCAM" },
        { "AKASO", "Akaso" },
        { "INSTA360", "Insta360" },
        { "Z CAM", "Z CAM" },
        { "IKONOSKOP", "Ikonoskop" },
        { "ARRI", "ARRI" },
        { "KINEFINITY", "Kinefinity" },
        { "ZEISS", "Zeiss" },
        { "ROLLEI", "Rollei" },
        { "THINKWARE", "Thinkware" },
        { "NEXTBASE", "Nextbase" },
        { "GARMIN", "Garmin" },
        { "PAPAGO", "Papago" },
        { "VIOFO", "Viofo" },
        { "MOTO", "Motorola" }
    };

        private static readonly Dictionary<string, string> SpecialFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "HEWLETT-PACKARD", "HP" },
        { "KONICA MINOLTA", "Konica Minolta" },
        { "Konica Minolta Camera, Inc.", "Konica Minolta" },
        { "LG ELECTRONICS", "LG" },
        { "Minolta Co., Ltd.", "Minolta" },
        { "NIKON CORPORATION", "Nikon" },
        { "CASIO COMPUTER CO.,LTD", "Casio" },
        { "EASTMAN KODAK COMPANY", "Kodak" },
        { "OLYMPUS CORPORATION", "Olympus" },
        { "OLYMPUS IMAGING CORP.", "Olympus" },
        { "OLYMPUS OPTICAL CO.,LTD", "Olympus" },
        { "SAMSUNG TECHWIN CO., LTD.", "Samsung" },
        { "SAMSUNG ELECTRONICS", "Samsung" },
        { "SAMSUNG TECHWIN", "Samsung" },
        { "SONY CORPORATION", "Sony" },
        { "SONY INTERACTIVE ENTERTAINMENT", "Sony" },
        { "SONY MOBILE COMMUNICATIONS INC.", "Sony" },
        { "SONY MOBILE COMMUNICATIONS", "Sony" },
        { "SONY ERICSSON MOBILE COMMUNICATIONS AB", "Sony Ericsson" },
        { "PENTAX CORPORATION", "Pentax" }
    };

        public static string GetDevice(string deviceMake, string deviceModel)
        {
            deviceMake = deviceMake.Trim();
            deviceModel = deviceModel.Trim();

            // If device model is empty, return the device make or an empty string
            if (string.IsNullOrWhiteSpace(deviceModel))
                return deviceMake ?? "";

            // If device make is empty, return the device model
            deviceMake ??= "";

            // Normalize the device make to uppercase for comparison
            string upperMake = deviceMake.ToUpperInvariant();

            if (CameraMakerMap.TryGetValue(upperMake, out var normalized))
            {
                deviceMake = normalized;
            }

            if (!string.IsNullOrEmpty(deviceMake) &&
                deviceModel.ToUpperInvariant().Contains(deviceMake.ToUpperInvariant()))
            {
                deviceMake = "";
            }

            foreach (var fix in SpecialFixes)
            {
                if (upperMake.Contains(fix.Key.ToUpperInvariant()) || deviceMake == fix.Key)
                {
                    deviceMake = fix.Value;
                    break;
                }
            }

            string result = string.IsNullOrEmpty(deviceMake)
                ? deviceModel
                : $"{deviceMake} {deviceModel.Replace(deviceMake + " ", "", StringComparison.OrdinalIgnoreCase)}".Trim();

            return result;
        }
    }
}
