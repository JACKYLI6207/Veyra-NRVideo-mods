using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace VeyraGpuBind {
static class Program {
  static readonly string[] SlotCn = { "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
  class Gpu { public string Name,PnpId,LegacyId,ChipId; public int Bus=int.MaxValue,Slot; public bool Integrated; }
  class DxgiCard { public string Name,ChipId; }

  static string SlotLabel(int n){ if(n>=1&&n<SlotCn.Length)return SlotCn[n]+"号显卡"; return n>0?n+"号显卡":"显卡"; }
  static string Esc(string s){ return (s??"").Replace("\\","\\\\").Replace("\"","\\\""); }
  static string Norm(string s){ return Regex.Replace((s??"").ToLowerInvariant(),@"\s+"," ").Trim(); }

  static int Main(string[] args){
    try{
      string root=FindRoot();
      var gpus=BuildList();
      if(gpus.Count==0){Console.WriteLine("找不到可用显卡。");return 2;}
      Console.WriteLine("Veyra GPU 绑定（PCIe 由上往下 = 一号 / 二号）");
      Console.WriteLine(root);Console.WriteLine();
      foreach(var g in gpus){
        string tag=g.Integrated?"[核显]":("["+SlotLabel(g.Slot)+"]");
        string bus=g.Bus==int.MaxValue?"?":g.Bus.ToString();
        Console.WriteLine("  {0} {1}  bus={2}  id={3}",tag,g.Name,bus,string.IsNullOrEmpty(g.LegacyId)?"(未映射)":g.LegacyId);
        if(!string.IsNullOrEmpty(g.PnpId))Console.WriteLine("       "+g.PnpId);
      }
      Console.WriteLine();
      int slot=0; string pnp=null; bool list=false;
      for(int i=0;i<args.Length;i++){
        if(args[i]=="--list")list=true;
        else if(args[i]=="--slot"&&i+1<args.Length)int.TryParse(args[++i],out slot);
        else if(args[i]=="--pnp"&&i+1<args.Length)pnp=args[++i];
      }
      if(list)return 0;
      Gpu chosen=null;
      if(!string.IsNullOrEmpty(pnp)){foreach(var g in gpus)if(string.Equals(g.PnpId,pnp,StringComparison.OrdinalIgnoreCase))chosen=g;}
      else{
        if(slot<=0){Console.Write("输入运算显卡槽位（1/2/…），Enter 取消: ");var line=Console.ReadLine();
          if(string.IsNullOrWhiteSpace(line)||!int.TryParse(line.Trim(),out slot)){Console.WriteLine("已取消。");return 1;}}
        foreach(var g in gpus)if(!g.Integrated&&g.Slot==slot)chosen=g;
      }
      if(chosen==null){Console.WriteLine("未找到该显卡。");return 3;}
      if(string.IsNullOrEmpty(chosen.LegacyId)){Console.WriteLine("无法映射到 Veyra DXGI id。");return 4;}
      string prefs=Path.Combine(root,"runtime_local","user-data-2.0.0","qml-preferences.v1.json");
      Directory.CreateDirectory(Path.GetDirectoryName(prefs));
      WritePrefs(prefs,chosen.LegacyId);
      File.WriteAllText(Path.Combine(Path.GetDirectoryName(prefs),"gpu-compute.v1.json"),
        "{\n  \"slot\": "+chosen.Slot+",\n  \"pnpId\": \""+Esc(chosen.PnpId)+"\",\n  \"legacyId\": \""+Esc(chosen.LegacyId)+"\",\n  \"name\": \""+Esc(chosen.Name)+"\"\n}\n",new UTF8Encoding(false));
      Console.WriteLine("已绑定: {0} | {1}",SlotLabel(chosen.Slot),chosen.Name);
      Console.WriteLine("monitorGpu="+chosen.LegacyId);
      Console.WriteLine("computeOnMonitorGpu=true");
      Console.WriteLine("请完全退出并重新打开 Veyra，再重新打开视频。");
      return 0;
    }catch(Exception ex){Console.WriteLine("失败: "+ex.Message);return 10;}
  }

  static string FindRoot(){
    string dir=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    var parent=Directory.GetParent(dir);
    if(parent!=null&&File.Exists(Path.Combine(parent.FullName,"veyra_qml_ui.exe")))return parent.FullName;
    if(File.Exists(Path.Combine(dir,"veyra_qml_ui.exe")))return dir;
    return parent!=null?parent.FullName:dir;
  }

  static void WritePrefs(string path,string monitorGpu){
    string json=File.Exists(path)?File.ReadAllText(path,Encoding.UTF8):"{}";
    json=Upsert(json,"monitorGpu","\""+Esc(monitorGpu)+"\"");
    json=Upsert(json,"computeOnMonitorGpu","true");
    File.WriteAllText(path,json,new UTF8Encoding(false));
  }
  static string Upsert(string json,string key,string literal){
    var re=new Regex("\""+Regex.Escape(key)+"\"\\s*:\\s*(true|false|null|\"([^\"\\\\]|\\\\.)*\"|[^,}\\]\\s]+)",RegexOptions.IgnoreCase);
    if(re.IsMatch(json))return re.Replace(json,"\""+key+"\": "+literal,1);
    int brace=json.LastIndexOf('}');
    if(brace<0)return "{\n  \""+key+"\": "+literal+"\n}\n";
    bool has=Regex.IsMatch(json.Substring(0,brace),"\"[^\"]+\"\\s*:");
    return json.Substring(0,brace)+(has?",\n  ":"\n  ")+"\""+key+"\": "+literal+"\n"+json.Substring(brace);
  }

  static List<Gpu> BuildList(){
    var wmi=new List<Gpu>();
    var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using(var s=new ManagementObjectSearcher("SELECT Name, PNPDeviceID, AdapterCompatibility FROM Win32_VideoController"))
    foreach(ManagementObject mo in s.Get()){
      string name=(mo["Name"]??"").ToString();
      string pnp=(mo["PNPDeviceID"]??"").ToString();
      string compat=(mo["AdapterCompatibility"]??"").ToString();
      if(string.IsNullOrWhiteSpace(name))continue;
      if(name.IndexOf("Basic Render",StringComparison.OrdinalIgnoreCase)>=0)continue;
      if(name.IndexOf("Microsoft Basic Display",StringComparison.OrdinalIgnoreCase)>=0)continue;
      if(!string.IsNullOrEmpty(pnp)&&!seen.Add(pnp))continue;
      wmi.Add(new Gpu{Name=name,PnpId=pnp,Integrated=IsIgpu(name,compat),Bus=BusFromRegistry(pnp),ChipId=ChipFromPnp(pnp)});
    }
    EnrichBusFromNvidiaSmi(wmi);
    wmi.Sort((a,b)=>{int c=a.Bus.CompareTo(b.Bus);return c!=0?c:string.Compare(a.PnpId,b.PnpId,StringComparison.OrdinalIgnoreCase);});
    int slot=0; foreach(var g in wmi)g.Slot=g.Integrated?0:++slot;

    var dxgi=ListDxgi();
    var chipCount=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var dxgiLegacy=new List<string>();
    foreach(var d in dxgi){
      int n; chipCount.TryGetValue(d.ChipId,out n);
      dxgiLegacy.Add(n>0?d.ChipId+"#"+n:d.ChipId);
      chipCount[d.ChipId]=n+1;
    }
    var used=new bool[dxgi.Count];
    // Match same chip in PCI-bus (WMI) order to DXGI entries of that chip in DXGI order.
    // For identical dual GPUs: 一号(bus小) gets first unused DXGI id for that chip, 二号 gets next.
    // Stock Veyra ordinal is DXGI high-perf order; we pin by consuming DXGI ids in that order.
    foreach(var g in wmi){
      if(string.IsNullOrEmpty(g.ChipId))continue;
      for(int i=0;i<dxgi.Count;i++){
        if(used[i])continue;
        if(!string.Equals(dxgi[i].ChipId,g.ChipId,StringComparison.OrdinalIgnoreCase))continue;
        used[i]=true; g.LegacyId=dxgiLegacy[i]; break;
      }
    }
    foreach(var g in wmi){
      if(!string.IsNullOrEmpty(g.LegacyId))continue;
      string want=Norm(g.Name);
      for(int i=0;i<dxgi.Count;i++){
        if(used[i])continue;
        string have=Norm(dxgi[i].Name);
        if(have==want||have.Contains(want)||want.Contains(have)){used[i]=true;g.LegacyId=dxgiLegacy[i];break;}
      }
    }
    wmi.Sort((a,b)=>{if(a.Integrated!=b.Integrated)return a.Integrated?1:-1;if(a.Slot!=b.Slot)return a.Slot.CompareTo(b.Slot);return a.Bus.CompareTo(b.Bus);});
    return wmi;
  }

  static string ChipFromPnp(string pnp){
    var m=Regex.Match(pnp??"",@"VEN_([0-9A-Fa-f]+)&DEV_([0-9A-Fa-f]+)(?:&SUBSYS_([0-9A-Fa-f]+))?",RegexOptions.IgnoreCase);
    if(!m.Success)return null;
    return string.Format("{0:x4}:{1:x4}:{2:x8}",Convert.ToUInt32(m.Groups[1].Value,16),Convert.ToUInt32(m.Groups[2].Value,16),m.Groups[3].Success?Convert.ToUInt32(m.Groups[3].Value,16):0u);
  }

  static List<DxgiCard> ListDxgi(){
    var list=new List<DxgiCard>();
    IntPtr factory=IntPtr.Zero;
    try{
      Guid iid=new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387");
      if(CreateDXGIFactory1(ref iid,out factory)!=0||factory==IntPtr.Zero)return list;
      for(uint i=0;;i++){
        IntPtr adapter=IntPtr.Zero;
        if(EnumAdapters1(factory,i,out adapter)!=0||adapter==IntPtr.Zero)break;
        IntPtr mem=Marshal.AllocHGlobal(320);
        try{
          if(GetDesc1Raw(adapter,mem)!=0)continue;
          uint flags=(uint)Marshal.ReadInt32(mem,304);
          uint ven=(uint)Marshal.ReadInt32(mem,256);
          if((flags&2)!=0||ven==0x1414)continue;
          uint dev=(uint)Marshal.ReadInt32(mem,260);
          uint sub=(uint)Marshal.ReadInt32(mem,264);
          string name=Marshal.PtrToStringUni(mem)??"";
          list.Add(new DxgiCard{Name=name.Trim(),ChipId=string.Format("{0:x4}:{1:x4}:{2:x8}",ven,dev,sub)});
        }finally{Marshal.FreeHGlobal(mem);Marshal.Release(adapter);}
      }
    }finally{if(factory!=IntPtr.Zero)Marshal.Release(factory);}
    return list;
  }

  static bool IsIgpu(string name,string compat){
    string n=(name+" "+compat).ToLowerInvariant();
    if(n.Contains("geforce")||n.Contains("rtx")||n.Contains("gtx")||n.Contains("radeon rx")||n.Contains("arc"))return false;
    return n.Contains("intel");
  }
  static int BusFromRegistry(string pnp){
    if(string.IsNullOrEmpty(pnp))return int.MaxValue;
    try{
      using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\"+pnp)){
        if(key==null)return int.MaxValue;
        string loc=key.GetValue("LocationInformation") as string;
        if(!string.IsNullOrEmpty(loc)){
          var m=Regex.Match(loc,@"\((\d+)\s*,\s*\d+\s*,\s*\d+\)\s*$");
          if(m.Success)return int.Parse(m.Groups[1].Value);
          m=Regex.Match(loc,@"(?:PCI\s*)?(?:bus|匯流排)\s*(\d+)",RegexOptions.IgnoreCase);
          if(m.Success)return int.Parse(m.Groups[1].Value);
        }
        object ui=key.GetValue("UINumber"); if(ui!=null)return Convert.ToInt32(ui);
      }
    }catch{}
    return int.MaxValue;
  }
  static void EnrichBusFromNvidiaSmi(List<Gpu> list){
    string smi=null;
    foreach(var p in new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"NVIDIA Corporation","NVSMI","nvidia-smi.exe"),Path.Combine(Environment.SystemDirectory,"nvidia-smi.exe")})
      if(File.Exists(p)){smi=p;break;}
    if(smi==null)return;
    try{
      var psi=new System.Diagnostics.ProcessStartInfo{FileName=smi,Arguments="--query-gpu=index,name,pci.bus_id --format=csv,noheader",UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true};
      string output; using(var p=System.Diagnostics.Process.Start(psi)){output=p.StandardOutput.ReadToEnd();p.WaitForExit(8000);}
      var rows=new List<KeyValuePair<int,string>>();
      foreach(var raw in (output??"").Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)){
        var parts=raw.Split(','); if(parts.Length<3)continue;
        var bm=Regex.Match(parts[parts.Length-1].Trim(),@":([0-9A-Fa-f]{2}):"); if(!bm.Success)continue;
        rows.Add(new KeyValuePair<int,string>(Convert.ToInt32(bm.Groups[1].Value,16),Norm(string.Join(",",parts,1,parts.Length-2).Trim())));
      }
      var used=new HashSet<int>(); foreach(var g in list)if(g.Bus!=int.MaxValue)used.Add(g.Bus);
      foreach(var g in list){
        if(g.Bus!=int.MaxValue)continue; string want=Norm(g.Name);
        foreach(var row in rows){if(used.Contains(row.Key))continue; if(row.Value==want||row.Value.Contains(want)||want.Contains(row.Value)){g.Bus=row.Key;used.Add(g.Bus);break;}}
      }
    }catch{}
  }

  [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid,out IntPtr factory);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnEnum1(IntPtr t,uint i,out IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnPref(IntPtr t,uint i,int p,ref Guid g,out IntPtr a);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnDesc(IntPtr t,IntPtr p);
  static int EnumAdapters1(IntPtr f,uint i,out IntPtr a){var fn=(FnEnum1)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(f),IntPtr.Size*12),typeof(FnEnum1));return fn(f,i,out a);}
  static int EnumByPref(IntPtr f,uint i,int p,ref Guid g,out IntPtr a){var fn=(FnPref)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(f),IntPtr.Size*29),typeof(FnPref));return fn(f,i,p,ref g,out a);}
  static int GetDesc1Raw(IntPtr a,IntPtr mem){var fn=(FnDesc)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(a),IntPtr.Size*10),typeof(FnDesc));return fn(a,mem);}
}
}

