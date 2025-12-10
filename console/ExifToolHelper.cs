using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace ImageDB
{
    public static class ExifToolHelper
    {
        private static Process? exiftoolProcess;
        private static StreamWriter? exiftoolInput;
        private static StreamReader? exiftoolOutput;
        private static readonly object exiftoolLock = new();
        private const string ReadyMarker = "{ready}";
        
        // PERFORMANCE: Increased buffer sizes for large metadata payloads
        // commandBuilder: 2KB for long file paths + command options
        // outputBuilder: 128KB for images with extensive metadata (regions, keywords, etc.)
        private static readonly StringBuilder commandBuilder = new StringBuilder(2048);
        private static readonly StringBuilder outputBuilder = new StringBuilder(131072);

        static ExifToolHelper()
        {
            StartExifTool();
        }

        public static bool CheckExiftool()
        {
            bool exiftoolReady = false;

            try
            {
                // Start a process to get the version of exiftool
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "exiftool.exe",
                        Arguments = "-ver", // Argument to fetch the version number
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                string version = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (string.IsNullOrEmpty(version))
                {
                    Console.WriteLine("[EXIFTOOL] - Exiftool is not installed or not found. Download exiftool from exiftool.org.");
                    exiftoolReady = false;
                }
                else
                {
                    Console.WriteLine($"[EXIFTOOL] - Exiftool version: {version}");
                    exiftoolReady = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[EXCEPTION] - " + ex.Message);
                exiftoolReady = false;
            }

            return exiftoolReady;
        }

        private static void StartExifTool()
        {
            exiftoolProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "exiftool.exe",
                    Arguments = "-stay_open True -@ -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            exiftoolProcess.StartInfo.EnvironmentVariables["LANG"] = "en_US.UTF-8";
            exiftoolProcess.Start();
            
            // PERFORMANCE: Increased stream buffer sizes from default 4KB to 64KB
            // Reduces system calls and improves throughput for large JSON responses
            exiftoolInput = new StreamWriter(exiftoolProcess.StandardInput.BaseStream, Encoding.UTF8, bufferSize: 65536);
            exiftoolOutput = new StreamReader(exiftoolProcess.StandardOutput.BaseStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 65536);

            Console.WriteLine("[EXIFTOOL] - Exiftool Process Started");
        }

        /// <summary>
        /// Optimized method to get both standard and struct metadata in a single ExifTool call.
        /// Returns tuple of (standardMetadata, structMetadata). If no regions/collections detected,
        /// structMetadata will be empty string.
        /// </summary>
        public static (string standard, string structData) GetExiftoolMetadataBoth(string filepath)
        {
            lock (exiftoolLock)
            {
                try
                {
                    if (exiftoolProcess == null || exiftoolProcess.HasExited)
                        StartExifTool();

                    // First call: Get standard metadata with -G1
                    commandBuilder.Clear();
                    commandBuilder.AppendLine("-json");
                    commandBuilder.AppendLine("-G1");
                    commandBuilder.AppendLine("-n");
                    commandBuilder.AppendLine(filepath);
                    commandBuilder.AppendLine("-execute");

                    exiftoolInput!.Write(commandBuilder.ToString());
                    exiftoolInput.Flush();

                    outputBuilder.Clear();
                    string? line;

                    while ((line = exiftoolOutput!.ReadLine()) != null)
                    {
                        if (line == ReadyMarker)
                            break;
                        outputBuilder.AppendLine(line);
                    }

                    string standardResult = outputBuilder.ToString().Trim();
                    standardResult = standardResult.Trim('[', ']');
                    standardResult = JsonConverter.ConvertNumericAndBooleanValuesToString(standardResult);

                    // Check if we need struct data (optimized: check JSON keys instead of string search)
                    string structResult = string.Empty;
                    if (standardResult.Contains("\"XMP-mwg-rs:") || 
                        standardResult.Contains("\"XMP-mwg-coll:") || 
                        standardResult.Contains("\"XMP-iptcExt:PersonInImage"))
                    {
                        // Second call: Get struct metadata
                        commandBuilder.Clear();
                        commandBuilder.AppendLine("-json");
                        commandBuilder.AppendLine("-struct");
                        commandBuilder.AppendLine("-XMP:RegionInfo");
                        commandBuilder.AppendLine("-XMP:Collections");
                        commandBuilder.AppendLine("-XMP:PersonInImageWDetails");
                        commandBuilder.AppendLine(filepath);
                        commandBuilder.AppendLine("-execute");

                        exiftoolInput.Write(commandBuilder.ToString());
                        exiftoolInput.Flush();

                        outputBuilder.Clear();
                        while ((line = exiftoolOutput.ReadLine()) != null)
                        {
                            if (line == ReadyMarker)
                                break;
                            outputBuilder.AppendLine(line);
                        }

                        structResult = outputBuilder.ToString().Trim();
                        structResult = structResult.Trim('[', ']');
                        structResult = JsonConverter.ConvertNumericAndBooleanValuesToString(structResult);
                    }

                    return (standardResult, structResult);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[EXCEPTION] - " + ex.Message);
                    return (string.Empty, string.Empty);
                }
            }
        }

        public static string GetExiftoolMetadata(string filepath, string mode)
        {

            lock (exiftoolLock)
            {
                try
                {
                    if (exiftoolProcess == null || exiftoolProcess.HasExited)
                        StartExifTool();

                    // Reuse StringBuilder, clear previous content
                    commandBuilder.Clear();

                    // Ensure the filepath is properly quoted if it contains spaces or special characters
                    string quotedFilepath = $"\"{filepath}\"";

                    // Add command options
                    commandBuilder.AppendLine($"-json");               // JSON output


                    if (mode == "mwg")  // Direct comparison, avoid ToLower() allocation
                    {
                        commandBuilder.AppendLine($"-struct");                     // Structure output
                        commandBuilder.AppendLine($"-XMP:RegionInfo");             // MWG RegionInfo
                        commandBuilder.AppendLine($"-XMP:Collections");            // MWG Collections
                        commandBuilder.AppendLine($"-XMP:PersonInImageWDetails");  // MWG Collections
                    }
                    else
                    {
                        commandBuilder.AppendLine($"-G1");                 // Group output by tag
                    }

                    commandBuilder.AppendLine($"-n");                      // Numeric output
                    //commandBuilder.AppendLine($"-charset UTF8");
                    //commandBuilder.AppendLine($"-charset filename=UTF8");      // Filename as UTF-8
                    commandBuilder.AppendLine(filepath);               // File path
                    commandBuilder.AppendLine("-execute");             // Execute the command

                    exiftoolInput!.Write(commandBuilder.ToString());
                    exiftoolInput.Flush();

                    // Reuse StringBuilder for output
                    outputBuilder.Clear();
                    string? line;

                    while ((line = exiftoolOutput!.ReadLine()) != null)
                    {
                        if (line == ReadyMarker)
                            break;

                        outputBuilder.AppendLine(line);
                    }

                    string result = outputBuilder.ToString().Trim();


                    // Type Safety: This is a workaround for Exiftool which adds the array brackets as well as sometimes changes the datatype of the values. All values will be returning as text.
                    result = result.Trim('[', ']');
                    result = JsonConverter.ConvertNumericAndBooleanValuesToString(result);

                    return result; // clean up array brackets
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[EXCEPTION] - " + ex.Message);
                    return string.Empty;
                }
            }
        }

        public static void Shutdown()
        {
            lock (exiftoolLock)
            {
                try
                {
                    exiftoolInput?.Write("-stay_open\nFalse\n");
                    exiftoolInput?.Flush();
                    exiftoolInput?.Close();
                    exiftoolOutput?.Close();

                    exiftoolProcess?.WaitForExit(2000);
                    exiftoolProcess?.Dispose();
                }
                catch { }
            }
        }
    }
}
