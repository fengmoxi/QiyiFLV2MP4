using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace QiyiFLV2MP4
{
    class Program
    {
        static bool _autoOverwrite;
        private static StringBuilder sortOutput = null;
        public static string inputPath = "";
        static int Main(string[] args)
        {
            int argCount = args.Length;
            int argIndex = 0;
            bool extractVideo = false;
            bool extractAudio = false;
            bool extractTimeCodes = false;
            string outputDirectory = null;
            
            Console.WriteLine();
            Console.WriteLine("QiyiFLV2MP4 v" + General.Version);
            Console.WriteLine("Copyright 2016 风漠兮");
            Console.WriteLine("http://www.fengmoxi.com/");
            Console.WriteLine();

            if (!File.Exists("js.dll"))
            {
                byte[] buffer = Properties.Resources.js;
                string path = AppDomain.CurrentDomain.BaseDirectory + "js.dll";
                FileStream FS = new FileStream(path, FileMode.Create);
                BinaryWriter BWriter = new BinaryWriter(FS);
                BWriter.Write(buffer, 0, buffer.Length);
                BWriter.Close();
            }

            if (!File.Exists("js32.dll"))
            {
                byte[] buffer = Properties.Resources.js32;
                string path = AppDomain.CurrentDomain.BaseDirectory + "js32.dll";
                FileStream FS = new FileStream(path, FileMode.Create);
                BinaryWriter BWriter = new BinaryWriter(FS);
                BWriter.Write(buffer, 0, buffer.Length);
                BWriter.Close();
            }

            if (!File.Exists("libgpac.dll"))
            {
                byte[] buffer = Properties.Resources.libgpac;
                string path = AppDomain.CurrentDomain.BaseDirectory + "libgpac.dll";
                FileStream FS = new FileStream(path, FileMode.Create);
                BinaryWriter BWriter = new BinaryWriter(FS);
                BWriter.Write(buffer, 0, buffer.Length);
                BWriter.Close();
            }

            if (!File.Exists("MP4Box.exe"))
            {
                byte[] buffer = Properties.Resources.MP4Box;
                string path = AppDomain.CurrentDomain.BaseDirectory + "MP4Box.exe";
                FileStream FS = new FileStream(path, FileMode.Create);
                BinaryWriter BWriter = new BinaryWriter(FS);
                BWriter.Write(buffer, 0, buffer.Length);
                BWriter.Close();
            }

            try
            {
                _autoOverwrite = true;
                extractVideo = true;
                extractAudio = true;

                if (argIndex != (argCount - 1))
                {
                    throw new Exception();
                }
                inputPath = args[argIndex];
            }
            catch
            {
                Console.WriteLine("Arguments: source_path");
                Console.WriteLine();
                return 1;
            }

            try
            {
                using (FLVFile flvFile = new FLVFile(Path.GetFullPath(inputPath)))
                {
                    if (outputDirectory != null)
                    {
                        flvFile.OutputDirectory = Path.GetFullPath(outputDirectory);
                    }
                    flvFile.ExtractStreams(extractAudio, extractVideo, extractTimeCodes, PromptOverwrite);
                    if ((flvFile.TrueFrameRate != null) || (flvFile.AverageFrameRate != null))
                    {
                        if (flvFile.TrueFrameRate != null)
                        {
                            Console.WriteLine("True Frame Rate: " + flvFile.TrueFrameRate.ToString());
                        }
                        if (flvFile.AverageFrameRate != null)
                        {
                            Console.WriteLine("Average Frame Rate: " + flvFile.AverageFrameRate.ToString());
                        }
                        Console.WriteLine();
                    }
                    if (flvFile.Warnings.Length != 0)
                    {
                        foreach (string warning in flvFile.Warnings)
                        {
                            Console.WriteLine("Warning: " + warning);
                        }
                        Console.WriteLine();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                return 1;
            }

            Process sortProcess;
            sortProcess = new Process();
            sortOutput = new StringBuilder("");
            sortProcess.StartInfo.FileName = "cmd.exe";
            sortProcess.StartInfo.UseShellExecute = false;// 必须禁用操作系统外壳程序  
            sortProcess.StartInfo.RedirectStandardOutput = true;
            sortProcess.StartInfo.RedirectStandardError = true; //重定向错误输出
            sortProcess.StartInfo.CreateNoWindow = true;  //设置不显示窗口
            sortProcess.StartInfo.RedirectStandardInput = true;
            sortProcess.StartInfo.Arguments = "/c mp4box.exe -add \"" + inputPath.Substring(0, inputPath.Length - 4) + ".264#trackID=1:par=1:1:name=\" -add \"" + inputPath.Substring(0, inputPath.Length - 4) + ".aac:name=\" -new \"" + inputPath.Substring(0, inputPath.Length - 4) + ".mp4\"";    //设定程式执行参数
            sortProcess.Start();
            sortProcess.BeginOutputReadLine();// 异步获取命令行内容  
            sortProcess.OutputDataReceived += new DataReceivedEventHandler(SortOutputHandler);
            Console.WriteLine("Packaging!Please wait with patience!");
            sortProcess.WaitForExit();//等待程序执行完退出进程
            sortProcess.Close();
            if (File.Exists(inputPath.Substring(0, inputPath.Length - 4) + ".aac"))
                File.Delete(inputPath.Substring(0, inputPath.Length - 4) + ".aac");
            if (File.Exists(inputPath.Substring(0, inputPath.Length - 4) + ".264"))
                File.Delete(inputPath.Substring(0, inputPath.Length - 4) + ".264");
            Console.WriteLine("Congratulations! " + inputPath + " has been converted to " + inputPath.Substring(0, inputPath.Length - 4) + ".mp4 successfully!");
            return 0;
        }

        private static bool PromptOverwrite(string path)
        {
            if (_autoOverwrite) return true;
            bool? overwrite = null;
            Console.Write("Output file \"" + Path.GetFileName(path) + "\" already exists, overwrite? (y/n): ");
            while (overwrite == null)
            {
                char c = Console.ReadKey(true).KeyChar;
                if (c == 'y') overwrite = true;
                if (c == 'n') overwrite = false;
            }
            Console.WriteLine(overwrite.Value ? "y" : "n");
            Console.WriteLine();
            return overwrite.Value;
        }
        private static void SortOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                Console.WriteLine(outLine.Data + Environment.NewLine);
            }
        }
    }
}
