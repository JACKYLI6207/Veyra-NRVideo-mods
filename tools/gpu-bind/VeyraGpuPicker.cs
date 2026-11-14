// Simple visual GPU picker for Veyra portable.
// csc /nologo /target:winexe /optimize+ /win32icon:veyra-app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /out:..\指定运算显卡.exe VeyraGpuPicker.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VeyraGpuPicker
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>Title bar + taskbar icon (same as Veyra).</summary>
        internal static Icon LoadVeyraIcon()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] paths =
            {
                Path.Combine(baseDir, "tools", "veyra-app.ico"),
                Path.Combine(baseDir, "veyra-app.ico"),
                Path.Combine(baseDir, "assets", "veyra-app-icon.ico")
            };
            foreach (string p in paths)
            {
                if (!File.Exists(p)) continue;
                return new Icon(p);
            }
            Icon fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            return fromExe ?? SystemIcons.Application;
        }
    }

    sealed class GpuItem
    {
        public string Name, PnpId, LegacyId, ChipId;
        public int Bus = int.MaxValue, Slot;
        public bool Integrated;
        public override string ToString()
        {
            if (Integrated) return Name + " [核显]";
            string bus = Bus == int.MaxValue ? "?" : Bus.ToString();
            return SlotLabel(Slot) + "  ·  " + Name + "  ·  PCI bus " + bus;
        }
        public static string SlotLabel(int n)
        {
            string[] cn = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
            if (n >= 1 && n < cn.Length) return cn[n] + "号显卡";
            return n > 0 ? n + "号显卡" : "显卡";
        }
    }

    sealed class MainForm : Form
    {
        readonly string root;
        readonly ListBox list;
        readonly Label status;
        readonly Button btnApply, btnLaunch, btnRefresh;

        public MainForm()
        {
            root = FindRoot();
            Text = "Veyra 指定运算显卡";
            ShowIcon = true;
            try { Icon = Program.LoadVeyraIcon(); } catch { ShowIcon = false; }
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(520, 360);
            Size = new Size(620, 420);
            Font = new Font("Segoe UI", 10f);
            BackColor = Color.FromArgb(32, 34, 38);
            ForeColor = Color.WhiteSmoke;

            var title = new Label
            {
                Text = "选择 Veyra 要用哪张显卡做增强 / 补帧 / 导出",
                Dock = DockStyle.Top,
                Height = 36,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 12, 0),
                Font = new Font("Segoe UI", 11f, FontStyle.Bold)
            };

            var hint = new Label
            {
                Text = "上槽（PCI bus 较小）= 一号，下槽 = 二号；列表一号在上。套用后请重开 Veyra，并重新打开视频。",
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(12, 0, 12, 0),
                ForeColor = Color.FromArgb(180, 186, 194)
            };

            list = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(24, 26, 30),
                ForeColor = Color.WhiteSmoke,
                Font = new Font("Segoe UI", 11f),
                ItemHeight = 28
            };
            list.DoubleClick += delegate { Apply(false); };

            status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                Padding = new Padding(12, 8, 12, 8),
                ForeColor = Color.FromArgb(160, 210, 160)
            };

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(10) };
            btnRefresh = MakeButton("刷新列表", 100);
            btnApply = MakeButton("只套用", 110);
            btnLaunch = MakeButton("套用并启动 Veyra", 160);
            btnLaunch.BackColor = Color.FromArgb(40, 130, 80);
            btnRefresh.Click += delegate { Reload(); };
            btnApply.Click += delegate { Apply(false); };
            btnLaunch.Click += delegate { Apply(true); };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            flow.Controls.Add(btnLaunch);
            flow.Controls.Add(btnApply);
            flow.Controls.Add(btnRefresh);
            bar.Controls.Add(flow);

            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
            pad.Controls.Add(list);

            Controls.Add(pad);
            Controls.Add(bar);
            Controls.Add(status);
            Controls.Add(hint);
            Controls.Add(title);

            Reload();
        }

        Button MakeButton(string text, int w)
        {
            return new Button
            {
                Text = text,
                Width = w,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 60, 70),
                ForeColor = Color.White,
                Margin = new Padding(6, 4, 6, 4),
                Cursor = Cursors.Hand
            };
        }

        void Reload()
        {
            list.Items.Clear();
            try
            {
                var gpus = GpuLogic.BuildList();
                GpuItem select = null;
                string current = ReadCurrentMonitorGpu();
                foreach (var g in gpus)
                {
                    if (g.Integrated) continue;
                    list.Items.Add(g);
                    if (!string.IsNullOrEmpty(current) && string.Equals(g.LegacyId, current, StringComparison.OrdinalIgnoreCase))
                        select = g;
                }
                if (list.Items.Count == 0)
                {
                    status.Text = "找不到独立显卡。";
                    status.ForeColor = Color.Salmon;
                    return;
                }
                if (select != null) list.SelectedItem = select;
                else list.SelectedIndex = 0;

                string curLabel = select != null ? select.ToString() : (string.IsNullOrEmpty(current) ? "尚未绑定 / 自动" : current);
                status.Text = "便携目录：\n" + root + "\n当前绑定：" + curLabel;
                status.ForeColor = Color.FromArgb(160, 210, 160);
            }
            catch (Exception ex)
            {
                status.Text = "读取失败：" + ex.Message;
                status.ForeColor = Color.Salmon;
            }
        }

        string ReadCurrentMonitorGpu()
        {
            string prefs = Path.Combine(root, "runtime_local", "user-data-2.0.0", "qml-preferences.v1.json");
            if (!File.Exists(prefs)) return null;
            var m = Regex.Match(File.ReadAllText(prefs, Encoding.UTF8),
                "\"monitorGpu\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        void Apply(bool launch)
        {
            var g = list.SelectedItem as GpuItem;
            if (g == null)
            {
                MessageBox.Show(this, "请先选择一张显卡。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.IsNullOrEmpty(g.LegacyId))
            {
                MessageBox.Show(this, "这张卡无法映射到 Veyra 内部 id。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try
            {
                if (GpuLogic.IsVeyraRunning())
                {
                    MessageBox.Show(this, "请先完全退出 Veyra（veyra_qml_ui），再套用。",
                        "Veyra 仍在运行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string prefs = GpuLogic.WritePrefs(root, g);
                status.Text = "已套用：" + g + "\nmonitorGpu=" + g.LegacyId + "\n" + prefs;
                status.ForeColor = Color.FromArgb(160, 210, 160);

                if (!launch)
                {
                    MessageBox.Show(this,
                        "已指定：\n" + g + "\n\n请启动 Veyra，并重新打开视频。\n设置里确认「同时用这块显卡运算」为开。",
                        "已套用", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                string exe = Path.Combine(root, "veyra_qml_ui.exe");
                if (!File.Exists(exe)) throw new Exception("找不到 " + exe);
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = root,
                    UseShellExecute = true
                });
                status.Text += "\n已启动 Veyra。请重新打开视频。";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string FindRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (File.Exists(Path.Combine(dir, "veyra_qml_ui.exe"))) return dir;
            var parent = Directory.GetParent(dir);
            if (parent != null && File.Exists(Path.Combine(parent.FullName, "veyra_qml_ui.exe")))
                return parent.FullName;
            return dir;
        }
    }

    static class GpuLogic
    {
        public static bool IsVeyraRunning()
        {
            return Process.GetProcessesByName("veyra_qml_ui").Length > 0;
        }

        public static string WritePrefs(string root, GpuItem g)
        {
            string prefs = Path.Combine(root, "runtime_local", "user-data-2.0.0", "qml-preferences.v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(prefs));
            string json = File.Exists(prefs) ? File.ReadAllText(prefs, Encoding.UTF8) : "{}";
            json = Upsert(json, "monitorGpu", "\"" + Esc(g.LegacyId) + "\"");
            json = Upsert(json, "computeOnMonitorGpu", "true");
            File.WriteAllText(prefs, json, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(prefs), "gpu-compute.v1.json"),
                "{\n  \"slot\": " + g.Slot + ",\n  \"pnpId\": \"" + Esc(g.PnpId) + "\",\n  \"legacyId\": \"" + Esc(g.LegacyId) + "\",\n  \"name\": \"" + Esc(g.Name) + "\"\n}\n",
                new UTF8Encoding(false));
            return prefs;
        }

        static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

        static string Upsert(string json, string key, string literal)
        {
            var re = new Regex("\"" + Regex.Escape(key) + "\"\\s*:\\s*(true|false|null|\"([^\"\\\\]|\\\\.)*\"|[^,}\\]\\s]+)", RegexOptions.IgnoreCase);
            if (re.IsMatch(json)) return re.Replace(json, "\"" + key + "\": " + literal, 1);
            int brace = json.LastIndexOf('}');
            if (brace < 0) return "{\n  \"" + key + "\": " + literal + "\n}\n";
            bool has = Regex.IsMatch(json.Substring(0, brace), "\"[^\"]+\"\\s*:");
            return json.Substring(0, brace) + (has ? ",\n  " : "\n  ") + "\"" + key + "\": " + literal + "\n" + json.Substring(brace);
        }

        public static List<GpuItem> BuildList()
        {
            var wmi = new List<GpuItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var s = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, AdapterCompatibility FROM Win32_VideoController"))
            foreach (ManagementObject mo in s.Get())
            {
                string name = (mo["Name"] ?? "").ToString();
                string pnp = (mo["PNPDeviceID"] ?? "").ToString();
                string compat = (mo["AdapterCompatibility"] ?? "").ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (name.IndexOf("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!string.IsNullOrEmpty(pnp) && !seen.Add(pnp)) continue;
                wmi.Add(new GpuItem
                {
                    Name = name,
                    PnpId = pnp,
                    Integrated = IsIgpu(name, compat),
                    Bus = BusFromRegistry(pnp),
                    ChipId = ChipFromPnp(pnp)
                });
            }
            EnrichBusFromNvidiaSmi(wmi);
            wmi.Sort((a, b) =>
            {
                int c = a.Bus.CompareTo(b.Bus);
                return c != 0 ? c : string.Compare(a.PnpId, b.PnpId, StringComparison.OrdinalIgnoreCase);
            });
            // bus 小 = 上槽一号；不反号（本机实测）。
            int slot = 0;
            foreach (var g in wmi) g.Slot = g.Integrated ? 0 : ++slot;

            var dxgi = ListDxgi();
            var chipCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var dxgiLegacy = new List<string>();
            foreach (var d in dxgi)
            {
                int n; chipCount.TryGetValue(d, out n);
                dxgiLegacy.Add(n > 0 ? d + "#" + n : d);
                chipCount[d] = n + 1;
            }
            var used = new bool[dxgi.Count];
            foreach (var g in wmi)
            {
                if (string.IsNullOrEmpty(g.ChipId)) continue;
                for (int i = 0; i < dxgi.Count; i++)
                {
                    if (used[i]) continue;
                    if (!string.Equals(dxgi[i], g.ChipId, StringComparison.OrdinalIgnoreCase)) continue;
                    used[i] = true;
                    g.LegacyId = dxgiLegacy[i];
                    break;
                }
            }
            // 列表：一号在上（slot 1 最先），核显在最后
            wmi.Sort((a, b) =>
            {
                if (a.Integrated != b.Integrated) return a.Integrated ? 1 : -1;
                if (a.Slot != b.Slot) return a.Slot.CompareTo(b.Slot);
                return a.Bus.CompareTo(b.Bus);
            });
            return wmi;
        }


        static string ChipFromPnp(string pnp)
        {
            var m = Regex.Match(pnp ?? "", @"VEN_([0-9A-Fa-f]+)&DEV_([0-9A-Fa-f]+)(?:&SUBSYS_([0-9A-Fa-f]+))?", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return string.Format("{0:x4}:{1:x4}:{2:x8}",
                Convert.ToUInt32(m.Groups[1].Value, 16),
                Convert.ToUInt32(m.Groups[2].Value, 16),
                m.Groups[3].Success ? Convert.ToUInt32(m.Groups[3].Value, 16) : 0u);
        }

        static List<string> ListDxgi()
        {
            var list = new List<string>();
            IntPtr factory = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
                if (CreateDXGIFactory1(ref iid, out factory) != 0 || factory == IntPtr.Zero) return list;
                for (uint i = 0; ; i++)
                {
                    IntPtr adapter;
                    if (EnumAdapters1(factory, i, out adapter) != 0 || adapter == IntPtr.Zero) break;
                    IntPtr mem = Marshal.AllocHGlobal(320);
                    try
                    {
                        if (GetDesc1(adapter, mem) != 0) continue;
                        uint flags = (uint)Marshal.ReadInt32(mem, 304);
                        uint ven = (uint)Marshal.ReadInt32(mem, 256);
                        if ((flags & 2) != 0 || ven == 0x1414) continue;
                        uint dev = (uint)Marshal.ReadInt32(mem, 260);
                        uint sub = (uint)Marshal.ReadInt32(mem, 264);
                        list.Add(string.Format("{0:x4}:{1:x4}:{2:x8}", ven, dev, sub));
                    }
                    finally { Marshal.FreeHGlobal(mem); Marshal.Release(adapter); }
                }
            }
            finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
            return list;
        }

        static bool IsIgpu(string name, string compat)
        {
            string n = (name + " " + compat).ToLowerInvariant();
            if (n.Contains("geforce") || n.Contains("rtx") || n.Contains("gtx") || n.Contains("radeon rx") || n.Contains("arc")) return false;
            return n.Contains("intel");
        }

        static string Norm(string s) { return Regex.Replace((s ?? "").ToLowerInvariant(), @"\s+", " ").Trim(); }

        static int BusFromRegistry(string pnp)
        {
            if (string.IsNullOrEmpty(pnp)) return int.MaxValue;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + pnp))
                {
                    if (key == null) return int.MaxValue;
                    string loc = key.GetValue("LocationInformation") as string;
                    if (!string.IsNullOrEmpty(loc))
                    {
                        var m = Regex.Match(loc, @"\((\d+)\s*,\s*\d+\s*,\s*\d+\)\s*$");
                        if (m.Success) return int.Parse(m.Groups[1].Value);
                        m = Regex.Match(loc, @"(?:PCI\s*)?(?:bus|匯流排)\s*(\d+)", RegexOptions.IgnoreCase);
                        if (m.Success) return int.Parse(m.Groups[1].Value);
                    }
                    object ui = key.GetValue("UINumber");
                    if (ui != null) return Convert.ToInt32(ui);
                }
            }
            catch { }
            return int.MaxValue;
        }

        static void EnrichBusFromNvidiaSmi(List<GpuItem> list)
        {
            string smi = null;
            foreach (var p in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe")
            }) if (File.Exists(p)) { smi = p; break; }
            if (smi == null) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = smi,
                    Arguments = "--query-gpu=index,name,pci.bus_id --format=csv,noheader",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                string output;
                using (var p = Process.Start(psi))
                { output = p.StandardOutput.ReadToEnd(); p.WaitForExit(8000); }
                var rows = new List<KeyValuePair<int, string>>();
                foreach (var raw in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = raw.Split(',');
                    if (parts.Length < 3) continue;
                    var bm = Regex.Match(parts[parts.Length - 1].Trim(), @":([0-9A-Fa-f]{2}):");
                    if (!bm.Success) continue;
                    rows.Add(new KeyValuePair<int, string>(Convert.ToInt32(bm.Groups[1].Value, 16),
                        Norm(string.Join(",", parts, 1, parts.Length - 2).Trim())));
                }
                var used = new HashSet<int>();
                foreach (var g in list) if (g.Bus != int.MaxValue) used.Add(g.Bus);
                foreach (var g in list)
                {
                    if (g.Bus != int.MaxValue) continue;
                    string want = Norm(g.Name);
                    foreach (var row in rows)
                    {
                        if (used.Contains(row.Key)) continue;
                        if (row.Value == want || row.Value.Contains(want) || want.Contains(row.Value))
                        { g.Bus = row.Key; used.Add(g.Bus); break; }
                    }
                }
            }
            catch { }
        }

        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int FnEnum1(IntPtr t, uint i, out IntPtr a);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int FnDesc(IntPtr t, IntPtr p);
        static int EnumAdapters1(IntPtr f, uint i, out IntPtr a)
        {
            var fn = (FnEnum1)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(f), IntPtr.Size * 12), typeof(FnEnum1));
            return fn(f, i, out a);
        }
        static int GetDesc1(IntPtr a, IntPtr mem)
        {
            var fn = (FnDesc)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(a), IntPtr.Size * 10), typeof(FnDesc));
            return fn(a, mem);
        }
    }
}
