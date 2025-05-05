using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageDB
{
    public static class DeviceHelper
    {
        /// Device Helper Class
        /// Normalizes device make and model strings
        /// Ref: https://www.exiftool.org/models.html
        /// Ref: https://exiftool.org/sample_images.html

        // To run RunTest, uncomment the line below in the Main method of Program.cs

        public static void RunTest()
        {
            Console.WriteLine("[TEST] - Device Helper");
            Console.WriteLine("----------------------");

            // Test cases for device make and model
            Dictionary<string, string> DeviceTestSet = new Dictionary<string, string>
            {
                { "Allied Vision Technologies", "GT3300C (02-2623A)" },
                { "Apple", "iPhone 11 Pro Max" },
                { "CASIO COMPUTER CO.,LTD", "EX-S770" },
                { "EASTMAN KODAK COMPANY", "KODAK CX7430 ZOOM DIGITAL CAMERA" },
                { "FUJIFILM", "FinePix F50fd" },
                { "KONICA MINOLTA", "DiMAGE X1" },
                { "Kodak", "" },
                { "MOTO", "1.2 MP" },
                { "samsung", "SM-G930P"},
                { "RICOH", "RICOH THETA SC2" },
                { "SAMSUNG", "SAMSUNG-SGH-I337" }
            };
            foreach (var device in DeviceTestSet)
            {
                Console.WriteLine(device.Key + " "+device.Value+" -> "+GetDevice(device.Key, device.Value));
            }
            Console.WriteLine("----------------------");
        }

        // Dictionary to map camera makers to their normalized names
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
            { "LOGITECH", "OnePlus" },
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
            { "AGFAPHOTO", "AgfaPhoto" },
            { "AGFA", "Agfa" },
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
            { "NORITSU KOKI", "Noritsu Koki" },
            { "MOTO", "Moto" }
        };

        // Dictionary to map camera makers to their normalized names with special cases    
        private static readonly Dictionary<string, string> SpecialFixes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "HEWLETT-PACKARD", "HP" },
            { "KONICA MINOLTA", "Konica Minolta" },
            { "KONICA MINOLTA CAMERA, Inc.", "Konica Minolta" },
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
            { "PENTAX CORPORATION", "Pentax" },
            { " CORPORATION", "" },
            { " TECHWIN CO.,LTD.", "" },
            { " CO.,LTD.", "" },
            { " ELECTRIC CO.,LTD.", "" },
            { " ELECTRIC CO.,LTD", "" },
            { " IMAGING CORP.", "" }
        };

        private static readonly Dictionary<string, string> CameraModelMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { " DIGITAL", "" },
            { " CAMERA", "" },
            { " DIGITAL CAMERA", "" },
            { " FILM SCANNER", "Film Scanner" },
            { " SCANNER", "Scanner" },
            { " PHOTO SCANNER", "Photo Scanner" },
            { "POWERSHOT", "PowerShot" },
            { "FINEPIX" , "FinePix"},
            { "COOLPIX", "Coolpix" },
            { "PHOTOSMART", "PhotoSmart" },
            { "CYBERSHOT", "Cybershot" },
            { "SCANJET", "ScanJet" },
            { "OFFICEJET", "OfficeJet" },
            { " DIMAGE", " DiMAGE" },
            { "DIMAGE ", "DiMAGE " },
            { "PANORAMIC", "Panoramic" },
            { "LUMIA", "Lumia" },
            { "THETA", "Theta" },
            { "STYLUS", "Stylus" },
            { "MAGICSCAN", "MagicScan" },
            { " ZOOM", "Zoom" }
        };


        public static string GetDevice(string deviceMake, string deviceModel)
        {
            string device   = String.Empty;
            
            deviceMake      = deviceMake.Trim();
            deviceModel     = deviceModel.Trim();

            // Device make and model to uppercase for comparison
            string upperMake = deviceMake.ToUpperInvariant();
            string upperModel = deviceModel.ToUpperInvariant();

            // Normalize the device make
            if (string.IsNullOrWhiteSpace(deviceMake)==false)
            {
                foreach (var fix in SpecialFixes)
                {
                    if (upperMake.Contains(fix.Key.ToUpperInvariant()) || deviceMake == fix.Key)
                    {
                        deviceMake = fix.Value;
                        break;
                    }
                }

                if (CameraMakerMap.TryGetValue(upperMake, out var normalizedMake))
                {
                    deviceMake = normalizedMake;
                }

                if (CameraModelMap.TryGetValue(upperMake, out var normalizedModelText))
                {
                    deviceMake = normalizedModelText;
                }
            }

            // If device model is empty, return the device make or an empty string, else normalize the model
            if (string.IsNullOrWhiteSpace(deviceModel))
            {
                return deviceMake ?? String.Empty;
            }
            else
            {

                foreach (var maker in CameraMakerMap)
                {
                    // Check if the input string contains the maker followed by a hyphen
                    if (upperModel.Contains(maker.Key + "-"))
                    {
                        // Replace the dash with a space after the maker
                        deviceModel = deviceModel.Replace(maker.Key + "-", maker.Value + " ");
                        break; // Exit loop after the first replacement
                    }
                }

                foreach (var maker in CameraMakerMap)
                {
                    // Check if the input string contains the maker followed by a hyphen
                    if (upperModel.Contains(maker.Key))
                    {
                        // Replace the dash with a space after the maker
                        deviceModel = deviceModel.Replace(maker.Key, maker.Value);
                        break; // Exit loop after the first replacement
                    }
                }

                foreach (var model in CameraModelMap)
                {
                    // Check if the input string contains the maker followed by a hyphen
                    if (upperModel.Contains(model.Key))
                    {
                        // Replace the dash with a space after the maker
                        deviceModel = deviceModel.Replace(model.Key, model.Value);
                    }
                }

            }

            // If device make is empty, return the device model
            deviceMake ??= String.Empty;

            // If device make is not empty and device model contains device make, remove device make from device model
            if (!string.IsNullOrEmpty(deviceMake) &&
                deviceModel.ToUpperInvariant().Contains(deviceMake.ToUpperInvariant()))
            {
                deviceMake = String.Empty;
            }

            device = deviceMake + " " + deviceModel;
            device = device.Trim();
            device = device.Replace("  ", " ");

            return device;
        }
    }
}
