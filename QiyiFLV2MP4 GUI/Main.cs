using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QiyiFLV2MP4_GUI
{
    public partial class Main : Form
    {
        public Main()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Multiselect = true;
            dlg.Filter = "FLV文件|*.FLV";
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                foreach (string s in dlg.FileNames)
                {
                    listBox1.Items.Add(s);
                }
            }
            if (listBox1.Items.Count == 0)
            {
                button4.Enabled = false;
            }
            else
            {
                button4.Enabled = true;
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                listBox1.Items.RemoveAt(listBox1.SelectedIndex);
            }
            catch
            {
                MessageBox.Show("请选择你要从列表中删除的文件！", "温馨提示");
            }
            if (listBox1.Items.Count == 0)
            {
                button4.Enabled = false;
            }
            else
            {
                button4.Enabled = true;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
            button4.Enabled = false;
        }

        private void button4_Click(object sender, EventArgs e)
        {

            DialogResult dr = MessageBox.Show("封装完成后是否删除FLV文件？", "提示", MessageBoxButtons.OKCancel);
            if (dr == DialogResult.OK)
            {
                //用户选择确认的操作
                foreach (string s in listBox1.Items)
                {
                    label3.Text = s;
                    if (!convert(s,true))
                    {
                        MessageBox.Show("转换失败，请尝试重试");
                    }
                }
            }
            else if (dr == DialogResult.Cancel)
            {
                //用户选择取消的操作
                foreach (string s in listBox1.Items)
                {
                    label3.Text = s;
                    if (!convert(s,false))
                    {
                        MessageBox.Show("转换失败，请尝试重试");
                    }
                }
            }
            button4.Enabled = false;
            label3.Text = listBox1.Items.Count.ToString() + "个FLV文件封装完毕！列表已清空！";
            listBox1.Items.Clear();
        }

        static bool _autoOverwrite;
        private static StringBuilder sortOutput = null;
        public static string inputPath = "";

        private bool convert(string args,bool delete)
        {
            bool extractVideo = false;
            bool extractAudio = false;
            bool extractTimeCodes = false;
            string outputDirectory = null;

            try
            {
                _autoOverwrite = true;
                extractVideo = true;
                extractAudio = true;
                inputPath = args;
            }
            catch
            {
                Console.WriteLine("Arguments: source_path");
                Console.WriteLine();
                return false;
            }

            richTextBox1.Text += "正在抽取视音频数据！\r\n";
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
                            richTextBox1.Text += "True Frame Rate: " + flvFile.TrueFrameRate.ToString() + "\r\n";
                        }
                        if (flvFile.AverageFrameRate != null)
                        {
                            richTextBox1.Text += "Average Frame Rate: " + flvFile.AverageFrameRate.ToString() + "\r\n";
                        }
                    }
                    if (flvFile.Warnings.Length != 0)
                    {
                        foreach (string warning in flvFile.Warnings)
                        {
                            richTextBox1.Text += "Warning: " + warning +"\r\n";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                richTextBox1.Text += "Error: " + ex.Message + "\r\n";
                return false;
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
            richTextBox1.Text += "封装为MP4中！请耐心等待！\r\n";
            sortProcess.BeginOutputReadLine();// 异步获取命令行内容  
            sortProcess.OutputDataReceived += new DataReceivedEventHandler(SortOutputHandler); // 为异步获取订阅事件
            sortProcess.WaitForExit();//等待程序执行完退出进程
            sortProcess.Close();
            if (File.Exists(inputPath.Substring(0, inputPath.Length - 4) + ".aac"))
                File.Delete(inputPath.Substring(0, inputPath.Length - 4) + ".aac");
            if (File.Exists(inputPath.Substring(0, inputPath.Length - 4) + ".264"))
                File.Delete(inputPath.Substring(0, inputPath.Length - 4) + ".264");
            if (delete)
                File.Delete(inputPath);
            richTextBox1.Text += "FLV文件" + inputPath + "已成功重封装为" + inputPath.Substring(0, inputPath.Length - 4) + ".mp4！\r\n";
            return true;
        }

        private void SortOutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            if (!String.IsNullOrEmpty(outLine.Data))
            {
                this.richTextBox1.Text += outLine.Data + Environment.NewLine;
            }
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

        private void Main_Load(object sender, EventArgs e)
        {
            Control.CheckForIllegalCrossThreadCalls = false;
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
        }

        private void listBox1_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, false))
            {
                String[] files = (String[])e.Data.GetData(DataFormats.FileDrop);
                foreach (String s in files)
                {
                    if(s.Substring(s.Length - 4, 4) != ".flv" && s.Substring(s.Length - 4, 4) != ".FLV")
                    {
                        MessageBox.Show("您拖入的文件 " + s + " 并不是FLV文件，请检查！", "温馨提示");
                    }
                    else
                    {
                        (sender as ListBox).Items.Add(s);
                    }
                }
            }
            if (listBox1.Items.Count == 0)
            {
                button4.Enabled = false;
            }
            else
            {
                button4.Enabled = true;
            }
        }

        private void listBox1_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void listBox1_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }

        private void richTextBox1_TextChanged(object sender, EventArgs e)
        {
            richTextBox1.SelectionStart = richTextBox1.Text.Length;
            richTextBox1.ScrollToCaret();
        }

        private void Main_FormClosed(object sender, FormClosedEventArgs e)
        {
            File.Delete("js.dll");
            File.Delete("js32.dll");
            File.Delete("libgpac.dll");
            File.Delete("MP4Box.exe");
        }
    }
}
