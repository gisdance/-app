using System;
using System.IO;
using System.Xml.Serialization;
namespace PiggyBuddy {
 public class Settings {
  public int Work=45, Rest=5, Scale=100, Volume=70;
  public bool Sound=true, Voice=false, Balloon=true, Bubble=true, Jump=true, Popup=false;
  public bool Topmost=true, AutoStart=false, Daily=false, Paused=false, EndAlert=true;
  public int StartMinute=540, EndMinute=1320;
  public string Message="起来活动一下吧，走走、伸展、看看远处。", Wav="";
  public double Left=-9999, Top=-9999;
  public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PiggyBuddy"); } }
  static string PathName { get { return Path.Combine(Folder,"settings.xml"); } }
  public static Settings Load() {
   try { using(var f=File.OpenRead(PathName)) { var s=(Settings)new XmlSerializer(typeof(Settings)).Deserialize(f);s.Validate();return s; } }
   catch { return new Settings(); }
  }
  public void Validate() {
   Work=Math.Max(1,Math.Min(240,Work));Rest=Math.Max(1,Math.Min(60,Rest));Scale=Math.Max(65,Math.Min(150,Scale));Volume=Math.Max(0,Math.Min(100,Volume));
   StartMinute=Math.Max(0,Math.Min(1439,StartMinute));EndMinute=Math.Max(0,Math.Min(1439,EndMinute));
   if(string.IsNullOrWhiteSpace(Message))Message="起来活动一下吧！";
   if(Message.Length>120)Message=Message.Substring(0,120);
   if(Wav==null)Wav="";
   if(double.IsNaN(Left)||double.IsInfinity(Left))Left=-9999;
   if(double.IsNaN(Top)||double.IsInfinity(Top))Top=-9999;
  }
  public void Save() {
   Validate();Directory.CreateDirectory(Folder);
   string tmp=PathName+".tmp";
   using(var f=File.Create(tmp))new XmlSerializer(typeof(Settings)).Serialize(f,this);
   if(File.Exists(PathName))File.Replace(tmp,PathName,null);else File.Move(tmp,PathName);
  }
 }
}
