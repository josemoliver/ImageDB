using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using LumenWorks.Framework.IO.Csv;

namespace ImageDB
{
    public static class DeviceHelper
    {
        ///<summary>
        /// DeviceHelper: Normalizes camera/device maker and model strings into 
        /// a consistent, human-friendly Device name used to populate de device field.
        /// (e.g., combine/normalize Make + Model into "Apple iPhone 11 Pro Max" or "Canon PowerShot G7").
        /// Ref: https://www.exiftool.org/models.html
        /// Ref: https://exiftool.org/sample_images.html
        ///</summary>

        /// To run RunTest, uncomment the line below in the Main method of Program.cs

        public static void RunTest()
        {
            Console.WriteLine("[TEST] - Device Helper");
            Console.WriteLine("----------------------");

            string csvPath = "Sample_DeviceList.csv";
            var DeviceTestSet = new List<KeyValuePair<string, string>>();

            if (File.Exists(csvPath))
            {
                try
                {
                    // Detect whether first non-empty line looks like a header containing "make" or "model"
                    bool hasHeader = false;
                    using (var peekReader = new StreamReader(csvPath))
                    {
                        string firstNonEmpty;
                        do
                        {
                            firstNonEmpty = peekReader.ReadLine();
                        } while (firstNonEmpty != null && string.IsNullOrWhiteSpace(firstNonEmpty));

                        if (!string.IsNullOrWhiteSpace(firstNonEmpty))
                        {
                            var header = firstNonEmpty.Trim();
                            if (header.IndexOf("make", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                header.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hasHeader = true;
                            }
                        }
                    }

                    // Use LumenWorks CsvReader to parse the file (handles quoted fields and embedded commas)
                    using (var sr = new StreamReader(csvPath))
                    using (var csv = new CsvReader(sr, hasHeader))
                    {
                        while (csv.ReadNextRecord())
                        {
                            // Read maker (first field)
                            string maker = csv.FieldCount > 0 ? (csv[0] ?? string.Empty).Trim() : string.Empty;
                            if (string.IsNullOrWhiteSpace(maker))
                            {
                                // skip records without a maker
                                continue;
                            }

                            // Join remaining fields into the model to preserve stray commas inside model
                            string model = string.Empty;
                            if (csv.FieldCount > 1)
                            {
                                var parts = new List<string>(csv.FieldCount - 1);
                                for (int i = 1; i < csv.FieldCount; i++)
                                {
                                    parts.Add((csv[i] ?? string.Empty).Trim());
                                }
                                model = string.Join(",", parts).Trim();
                            }

                            // Add to test set (allow duplicates)
                            DeviceTestSet.Add(new KeyValuePair<string, string>(maker, model));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to read or parse CSV file: " + ex.Message);
                    Console.WriteLine("Falling back to embedded test data.");
                    DeviceTestSet = GetDefaultTestSet();
                }
            }
            else
            {
                Console.WriteLine($"CSV file '{csvPath}' not found. Using embedded test data.");
                DeviceTestSet = GetDefaultTestSet();
            }

            foreach (var device in DeviceTestSet)
            {
                Console.WriteLine(device.Key + " " + device.Value + " -> " + GetDevice(device.Key, device.Value));
            }
            Console.WriteLine("----------------------");
        }

        private static List<KeyValuePair<string, string>> GetDefaultTestSet()
        {
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Allied Vision Technologies", "GT3300C (02-2623A)"),
                new KeyValuePair<string, string>("Apple", "iPhone 11 Pro Max"),
                new KeyValuePair<string, string>("CASIO COMPUTER CO.,LTD", "EX-S770"),
                new KeyValuePair<string, string>("EASTMAN KODAK COMPANY", "KODAK CX7430 ZOOM DIGITAL CAMERA"),
                new KeyValuePair<string, string>("FUJIFILM", "FinePix F50fd"),
                new KeyValuePair<string, string>("KONICA MINOLTA", "DiMAGE X1"),
                new KeyValuePair<string, string>("Kodak", ""),
                new KeyValuePair<string, string>("MOTO", "1.2 MP"),
                new KeyValuePair<string, string>("Canon", "EOS-1D"),
                new KeyValuePair<string, string>("RICOH", "RICOH THETA SC2"),
                new KeyValuePair<string, string>("SAMSUNG", "SAMSUNG-SGH-I337"),
                new KeyValuePair<string, string>("NIKON", "COOLPIX AW100")
            };
        }

        // Dictionary to map camera makers to their normalized names
        private static readonly Dictionary<string, string> CameraMakerMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ACER", "Acer" },
            { "AGFA", "Agfa" },
            { "AGFA GEVAERT", "Agfa-Gevaert" },
            { "AGFAPHOTO", "AgfaPhoto" },
            { "AIPTEK", "AIPTEK" },
            { "AKASO", "Akaso" },
            { "APPLE", "Apple" },
            { "ARRI", "ARRI" },
            { "ASUS", "Asus" },
            { "BBK IMAGING", "BBK Imaging" },
            { "BELL & HOWELL", "Bell & Howell" },
            { "BENQ", "BenQ" },
            { "BLACKBERRY", "BlackBerry" },
            { "BLACKMAGIC DESIGN", "Blackmagic Design" },
            { "BUSHNELL", "Bushnell" },
            { "CAMERA", "Camera" },
            { "CANON", "Canon" },
            { "CASIO", "Casio" },
            { "CONCORD", "Concord" },
            { "DELL", "Dell" },
            { "DIGILIFE", "DigiLife" },
            { "DJI", "DJI" },
            { "DOCOMO", "DoCoMo" },
            { "DS GLOBAL", "DS Global" },
            { "DXG", "DGX" },
            { "EPSON", "Epson" },
            { "FLIR", "FLIR" },
            { "FUJIFILM", "Fujifilm" },
            { "FUJITSU", "Fujitsu" },
            { "GARMIN", "Garmin" },
            { "GEDSC", "GEDSC" },
            { "GENERAL IMAGING", "General Imaging" },
            { "GENIUS", "Genius" },
            { "GOPRO", "GoPro" },
            { "GOOGLE", "Google" },
            { "GRANDTECH", "GrandTech" },
            { "HASSELBLAD", "Hasselblad" },
            { "HELIO", "Helio" },
            { "HEWLETT-PACKARD", "HP" },
            { "HITACHI", "Hitachi" },
            { "HOLGA", "Holga" },
            { "HONOR", "Honor" },
            { "HP", "HP" },
            { "HUAWEI", "Huawei" },
            { "IKONOSKOP", "Ikonoskop" },
            { "INSTA360", "Insta360" },
            { "IQOO", "iQOO" },
            { "JENOPTIK", "Jenoptik" },
            { "KDDI-CA", "KDDI" },
            { "KINEFINITY", "Kinefinity" },
            { "KODAK", "Kodak" },
            { "KONICA MINOLTA", "Konica Minolta" },
            { "KONICA", "Konica" }, 
            { "KYOCERA", "Kyocera" },
            { "LEAF", "Leaf" },
            { "LEICA", "Leica" },
            { "LENOVO", "Lenovo" },
            { "LG", "LG" },
            { "LG CYON", "LG Cyon" },
            { "LOGITECH", "Logitech" },
            { "LOMOGRAPHY", "Lomography" },
            { "LUMICRON", "Lumicron" },
            { "MAGION", "Magion" },
            { "MEDION", "Medion" },
            { "MEIKE", "Meike" },
            { "MINOLTA", "Minolta" },
            { "MITAC", "MiTAC" },
            { "MOTO", "Moto" },
            { "MOTOROLA", "Motorola" },
            { "MOULTRIE", "Moultrie" },
            { "NEXTBASE", "Nextbase" },
            { "NIKON", "Nikon" },
            { "NINTENDO", "Nintendo" },
            { "NOKIA", "Nokia" },
            { "NORITSU KOKI", "Noritsu Koki" },
            { "ODYS", "Odys" },
            { "OLYMPUS", "Olympus" },
            { "OM SYSTEM", "OM System" },
            { "OMG LIFE", "OMG Life" },
            { "ONEPLUS", "OnePlus" },
            { "PANASONIC", "Panasonic" },
            { "PANTECH", "Pantech" },
            { "PALM", "Palm" },
            { "PAPAGO", "Papago" },
            { "PARROT", "Parrot" },
            { "PENTACON", "Pentacon" },
            { "PENTAX", "Pentax" },
            { "PHASE ONE", "Phase One" },
            { "PIONEER", "Pioneer" },
            { "POLAROID", "Polaroid" },
            { "PRAKTICA", "Praktica" },
            { "REALME", "Realme" },
            { "RED DIGITAL CINEMA", "RED Digital Cinema" },
            { "RECONYX", "Reconyx" },
            { "RICOH", "Ricoh" },
            { "ROLLEI", "Rollei" },
            { "SAMSUNG", "Samsung" },
            { "SANYO", "Sanyo" },
            { "SEGA", "Sega" },
            { "SHARP", "Sharp" },
            { "SIGMA", "Sigma" },
            { "SIPIX", "SiPix" },
            { "SJCAM", "SJCAM" },
            { "SKANHEX", "Skanhex" },
            { "SONY ERICSSON", "Sony Ericsson" },
            { "SONY", "Sony" },
            { "SPECTRUM IMAGING", "Spectrum Imaging" },
            { "TAMRON", "Tamron" },
            { "THINKWARE", "Thinkware" },
            { "TOKINA", "Tokina" },
            { "TOSHIBA", "Toshiba" },
            { "TRAVELER", "Traveler" },
            { "VIOFO", "Viofo" },
            { "VISTAQUEST", "VistaQuest" },
            { "VIVO", "Vivo" },
            { "VIVAX", "Vivax" },
            { "VIVITAR", "Vivitar" },
            { "VODAFONE", "Vodafone" },
            { "VTECH", "VTech" },
            { "WWL", "WWL" },
            { "XIAOMI", "Xiaomi" },
            { "XIAOYI", "Xiaoyi" },
            { "YAKUMO", "Yakumo" },
            { "YASHICA", "Yashica" },
            { "Z CAM", "Z CAM" },
            { "ZEISS", "Zeiss" }
        };

        // Dictionary to map camera makers to their normalized names with special cases    
        private static readonly Dictionary<string, string> SpecialFixes = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ASASHI OPTICAL", "Pentax" },
            { "BENQ_E72", "BenQ" },
            { "CASIO COMPUTER", "Casio" },
            { "CONCORD CAMERA", "Concord" },
            { "DAISY MULTIMEDIA", "Daisy Multimedia" },
            { "DXG TECH", "DGX" },
            { "EASTMAN KODAK", "Kodak" },
            { "EASTMAN-KODAK", "Kodak" },
            { "FLIR SYSTEMS", "FLIR" },
            { "FUJI PHOTO", "Fujifilm" },
            { "GATEWAY", "Gateway" },
            { "HEWLETT PACKARD", "HP" },
            { "HEWLETT-PACKARD", "HP" },
            { "JENIMAGE", "Jenimage" },
            { "JENOPTIFIED", "Jenoptified" },
            { "JK IMAGING", "JK Imaging" },
            { "HITACHI LIVING SYSTEMS", "Hitachi" },
            { "KONICA MINOLTA", "Konica Minolta" },
            { "KONICA CORPORATION", "Konica" },
            { "LEGEND", "Legend DSC" },
            { "LEICA CAMERA", "Leica" },
            { "LG ELEC", "LG" },
            { "LG Mobile", "LG" },
            { "LG_ELECTRONICS", "LG" },
            { "LUMICRON TECHNOLOGY", "Lumicron" },
            { "MADE BY POLAROID", "Polaroid" },
            { "MAGINON", "Magion" },
            { "MEDION OPTICAL", "MEDION" },
            { "MAMIYA", "Mamiya" },
            { "MAMIYA-OP", "Mamiya" }, 
            { "MOTOROLA KOREA", "Motorola" },
            { "MOTOROLA MOBILITY", "Motorola" },
            { "MSM6500", "Samsung" },
            { "MINOLTA", "Minolta" },
            { "OM Digital Solutions", "OM System" },
            { "OLYMPUS IMAGING", "Olympus" },
            { "OLYMPUS OPTICAL", "Olympus" },
            { "OPPO", "OPPO" },
            { "PANTECH WIRELESS", "Pantech" },
            { "PENTACON GERMANY", "Pentacon" },
            { "Q6065BSNAXHZ33504", "Samsung" },
            { "PENTAX CORPORATION", "Pentax" },
            { "PENTAX ", "Pentax" },
            { "RESEARCH IN MOTION", "RIM" },
            { "RICOH IMAGING", "Ricoh" },
            { "RICOH IMAGING COMPANY", "Ricoh" },
            { "SAMSUNG ELEC", "Samsung" },
            { "SAMSUNG TECHWIN", "Samsung Techwin" },
            { "SANYO ELECTRIC", "Sanyo" },
            { "SEIKO EPSON", "Seiko Epson" },
            { "SKANHEX", "Skanhex" },
            { "SONY ERICSSON", "Sony Ericsson" },
            { "SONY INTERACTIVE ENTERTAINMENT", "Sony" },
            { "SONY MOBILE COMMUNICATIONS", "Sony" },
            { "SONY-ERICSSON", "Sony Ericsson" },
            { "SONYERICSSON", "Sony Ericsson" },
            { "SUPRA", "Supra" },
            { "TRAVELER OPTICAL", "Traveler Optical" },
            { "YAKUMO", "Yakumo" }
        };



        private static readonly Dictionary<string, string> CameraModelMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "POWERSHOT", "PowerShot " },
            { "SAMSUNG", "Samsung" },
            { "KODAK", "Kodak " },
            { "POLARIOD", "Polaroid " },
            { "FINEPIX" , "FinePix "},
            { "REBEL", "Rebel " },
            { "COOLPIX", "Coolpix " },
            { "PERFECTION", "Perfection " },
            { "CONCORD", "Concord " },
            { "CANOSCAN", "CanoScan " },
            { "PHOTOSMART", "PhotoSmart " },
            { "CYBERSHOT", "Cybershot " },
            { "SCANJET", "ScanJet " },
            { "OFFICEJET", "OfficeJet " },
            { "DIMAGE ", "DiMAGE " },
            { "PANORAMIC", "Panoramic " },
            { "EXILIM", "Exilim " },
            { "GALAXY", "Galaxy " },
            { "PIXMA", "Pixma " },
            { "IXUS", "IXUS " },
            { "IPHONE", "iPhone " },
            { "SURESHOT", "SureShot " },
            { "SMARTSHOT", "SmartShot " },
            { "ZOOMATE", "Zoomate " },
            { "VIVITAR", "Vivitar " },
            { "VIVICAM", "ViviCam " },
            { "DROID", "Droid " },
            { "LUMIA", "Lumia " },
            { "THETA", "Theta " },
            { "STYLUS", "Stylus " },
            { "MAGICSCAN", "MagicScan" },
            { "EASYSHARE", "EasyShare" },
            { "HYPERFIRE", "HyperFire" },
            { "DIGITAL SCIENCE", "Digital Science" },
            { "PENTAX ","Pentax " },
            { "PLAYFULL", "Playfull " },
            { "PLAYSPORT", "PlaySport " },
            { "PIXPRO", "PixPro " },
            { "COOLSCAN", "Coolscan " },
            { "IZONE", "iZone" },
            { "ALPHA SWEET DIGITAL", "Alpha Sweet Digital" },
            { "SPEED STAR", "Speed Star" },
            { "ALPHA", "Alpha" },
            { "MAXXUM", "Maxxum" },
            { "ASUS_", "Asus " },
            { "EYE_Q", "Eye-Q " },
            { "EYEQ", "Eye-Q " },
            { " EOS", " EOS " },
            { " MEGAPIXEL", " Megapixel" },
            { " MEGAPIXELS", " Megapixels" },
            { " SENSOR", " Sensor" },
            { " ZOOM", " Zoom" },
            { " FILM SCANNER", " Film Scanner" },
            { " SCANNER", " Scanner" },
            { " PHOTO SCANNER", " Photo Scanner" },
            { " DIGITAL CAMERA", " Digital Camera" },
            { " DIGITAL", " Digital" },
            { " CAMERA", " Camera" },
            { " WIRELESS", " Wireless" },
            { " DUAL LENS", " Dual Lens" },
            { " CELLPHONE", " Cellphone" }
        };


        /// <summary>
        /// Normalize and compose a human-friendly device name from raw EXIF maker and model strings.
        /// 
        /// This method performs extensive cleaning and normalization:
        /// - Makes inputs null-safe and trims whitespace; treats a single &quot;*&quot; model as empty.
        /// - Strips angle brackets, question marks and common corporate suffixes (e.g., &quot;Inc.&quot;, &quot;Co., Ltd.&quot;) from the model.
        /// - Applies pre-defined mapping dictionaries to normalize maker names (<see cref="CameraMakerMap"/> and <see cref="SpecialFixes"/>)
        ///   and to normalize common model tokens and prefixes (<see cref="CameraModelMap"/>).
        /// - Handles cases where the maker appears inside the model (removes duplicate maker mentions),
        ///   and performs a set of final special-case string adjustments (e.g., &quot;HTC-&quot; &rarr; &quot;HTC &quot;).
        /// 
        /// The returned string is a single, human-readable device name (for example, &quot;Apple iPhone 11 Pro Max&quot; or
        /// &quot;Canon PowerShot G7&quot;). If both inputs are empty or normalization yields no meaningful output, an empty string is returned.
        /// </summary>
        /// <param name="deviceMake">The raw maker string from metadata (may be null or empty).</param>
        /// <param name="deviceModel">The raw model string from metadata (may be null or empty).</param>
        /// <returns>
        /// A normalized device name suitable for display or indexing. Returns an empty string if no maker or model information is available.
        /// </returns>
        public static string GetDevice(string deviceMake, string deviceModel)
        {
            // Make method null-safe
            deviceMake ??= string.Empty;
            deviceModel ??= string.Empty;

            string device = String.Empty;

            // Remove < > characters from device make and model
            deviceMake = deviceMake.Replace("<", "").Replace(">", "");
            deviceModel = deviceModel.Replace("<", "").Replace(">", "");

            // Remove ? characters from device make and model
            deviceMake = deviceMake.Replace("?", "");
            deviceModel = deviceModel.Replace("?", "");

            // Replace underscores with spaces in device make and model
            deviceMake = deviceMake.Replace("_", " ");
            deviceModel = deviceModel.Replace("_", " ");

            // Trim whitespace from device make and model
            deviceMake = deviceMake.Trim();
            deviceModel = deviceModel.Trim();

            if (deviceModel == "*") { deviceModel = String.Empty; }


            // List of common suffixes
            string[] suffixes = {
                "TECHNOLOGY", "ELECTRIC", "IMAGING", "CAMERA", "OPTICAL",
                "CORPORATION", "COMPANY", "INTERNATIONAL", "GMBH",
                "CORP", "INC", "CO", "LTD", "\\(C\\)", "\\(R\\)"
            };

            // Build a single regex pattern to remove any trailing suffix combination
            string pattern = @"(?:\b(?:" + string.Join("|", suffixes) + @")(?:(?:[\s\.,\-]*)?))*$";

            // Apply regex to remove suffixes at the end
            deviceMake = Regex.Replace(deviceMake, pattern, "", RegexOptions.IgnoreCase);

            // Final cleanup of punctuation and whitespace
            deviceMake = Regex.Replace(deviceMake, @"[.,]", "");
            deviceMake = Regex.Replace(deviceMake, @"\s+", " ").Trim();


            deviceMake = deviceMake.Trim();
            
            // If both device make and model are empty, return an empty string
            if (string.IsNullOrWhiteSpace(deviceModel) && string.IsNullOrWhiteSpace(deviceMake))
            {
                return string.Empty;
            }

            // Normalize the device make
            if (!string.IsNullOrWhiteSpace(deviceMake))
            {
                // Apply special fixes first
                foreach (var fix in SpecialFixes)
                {
                    if (deviceMake.ToUpperInvariant().Contains(fix.Key))
                    {
                        deviceMake = fix.Value;
                        break;
                    }
                }

                // Apply general maker normalization
                foreach (var maker in CameraMakerMap)
                {
                    if (deviceMake.ToUpperInvariant().Contains(maker.Key))
                    {
                        deviceMake = maker.Value;
                        break;
                    }
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
                    if (deviceModel.ToUpperInvariant().Contains(maker.Key + "-"))
                    {
                        // Replace the dash with a space after the maker
                        deviceModel = Regex.Replace(deviceModel, Regex.Escape(maker.Key + "-"), maker.Value + " ", RegexOptions.IgnoreCase);
                        break; // Exit loop after the first replacement
                    }
                }

                foreach (var model in CameraModelMap)
                {
                    // Check if the model string contains the model key
                    if (deviceModel.ToUpperInvariant().Contains(model.Key))
                    {
                        // Replace the model value with the normalized model name
                        deviceModel = Regex.Replace(deviceModel, Regex.Escape(model.Key), model.Value, RegexOptions.IgnoreCase);
                    }
                }

                // Remove extra spaces in device model
                deviceModel = deviceModel.Replace("  ", " ");

            }

            // If device make is empty, return the device model
            deviceMake ??= String.Empty;

            // If device make is not empty and device model contains device make, remove device make.
            if (!string.IsNullOrEmpty(deviceMake) &&
                deviceModel.ToUpperInvariant().Contains(deviceMake.ToUpperInvariant()))
            {
                deviceModel = Regex.Replace(deviceModel, Regex.Escape(deviceMake), deviceMake, RegexOptions.IgnoreCase);
                deviceMake = String.Empty;
            }

            device = deviceMake + " " + deviceModel;
            device = device.Trim();
            device = device.Replace("  ", " ");

            // Final special cases
            device = device.Replace("Kodak Kodak", "Kodak");
            device = device.Replace("Hewlett Packard HP", "HP");
            if (device == "Digimax101") { device = "Samsung Digimax 101"; }
            if (device.StartsWith("HTC-")) { device = device.Replace("HTC-", "HTC "); }
            if (device.StartsWith("HTC_")) { device = device.Replace("HTC_", "HTC "); }
            if (device.StartsWith("ZTE-")) { device = device.Replace("ZTE-", "ZTE "); }
            if (device.StartsWith("hp ")) { device = device.Replace("hp ", "HP "); }
            if (device.StartsWith("Canon ")) { device = device.Replace("EOS-", "EOS "); }

            return device;
        }
    }
}
