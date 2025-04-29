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
            exiftoolInput = exiftoolProcess.StandardInput;
            exiftoolOutput = exiftoolProcess.StandardOutput;

            Console.WriteLine("[EXIFTOOL] - Exiftool Process Started");
        }

        public static string GetExiftoolMetadata(string filepath)
        {

            lock (exiftoolLock)
            {
                try
                {
                    if (exiftoolProcess == null || exiftoolProcess.HasExited)
                        StartExifTool();

                    var cmd = new StringBuilder();

                    // Ensure the filepath is properly quoted if it contains spaces or special characters
                    string quotedFilepath = $"\"{filepath}\"";

                    // Add command options
                    cmd.AppendLine($"-json");   // JSON output
                    cmd.AppendLine($"-G1");     // Group output by tag
                    cmd.AppendLine($"-n");      // Numeric output
                                                //cmd.AppendLine($"-charset UTF8");
                                                //cmd.AppendLine($"-charset filename=UTF8");      // Filename as UTF-8
                    cmd.AppendLine(filepath);   // File path
                    cmd.AppendLine("-execute"); // Execute the command

                    exiftoolInput!.Write(cmd.ToString());
                    exiftoolInput.Flush();

                    var outputBuilder = new StringBuilder();
                    string? line;

                    while ((line = exiftoolOutput!.ReadLine()) != null)
                    {
                        if (line == ReadyMarker)
                            break;

                        outputBuilder.AppendLine(line);
                    }

                    string result = outputBuilder.ToString().Trim();


                    // This is a workaround for Exiftool which adds the array brackets as well as sometimes changes the datatype of the values. All values will be returning as text.
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
