using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Media;
using System.Speech.Synthesis;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace PiggyBuddy {
 public static class Entry {
  [STAThread] public static void Main() {
   bool created;
   using(var mutex=new Mutex(true,"Local\\PiggyBuddy.Windows.v2",out created)) {
    if(!created){ MessageBox.Show("桌宠已经在运行。请在任务栏右下角的托盘中双击图标找回。","抱猪提醒");return; }
    var app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;
    app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){
     Log(e.Exception);MessageBox.Show("遇到问题，详情已保存到：\n"+Path.Combine(Settings.Folder,"error.log"),"抱猪提醒");e.Handled=true;app.Shutdown(1);
    };
    try { app.Run(new PetWindow()); } catch(Exception ex){Log(ex);MessageBox.Show("启动失败："+ex.Message+"\n可将 error.log 发回排查。","抱猪提醒");}
   }
  }
  public static void Log(Exception ex){try{Directory.CreateDirectory(Settings.Folder);File.AppendAllText(Path.Combine(Settings.Folder,"error.log"),DateTime.Now.ToString("s")+" "+ex+Environment.NewLine);}catch{}}
 }
 public sealed class PetWindow:Window {
  Settings cfg;
  TimerCore core;
  Forms.NotifyIcon tray;
  Forms.ToolStripMenuItem pauseMenu;
  DispatcherTimer ticker;
  Stopwatch watch=new Stopwatch();
  double lastTime, jumpUntil, bubbleUntil, previewUntil;
  bool allowed=true;
  int lastDay;
  BitmapSource idleSprite,alertSprite;
  Image pet;
  TextBlock clock,phase,bubbleText;
  Border bubble;
  Button pauseButton;
  TranslateTransform petOffset=new TranslateTransform();
  ScaleTransform petBreath=new ScaleTransform(1,1);
  Window settingsWindow,reminderWindow;
  SpeechSynthesizer speech;
  SoundPlayer sound;
  string speechStatus="";
  const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
  public PetWindow() {
   cfg=Settings.Load();core=new TimerCore(cfg.Work*60,cfg.Rest*60);core.Paused=cfg.Paused;
   Title="抱猪提醒";WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Topmost=cfg.Topmost;
   FontFamily=new FontFamily("Microsoft YaHei UI");UseLayoutRounding=true;
   string layout=@"<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Width='260' Height='350'>
<Grid.RowDefinitions><RowDefinition Height='76'/><RowDefinition Height='220'/><RowDefinition Height='54'/></Grid.RowDefinitions>
<Border x:Name='Bubble' Margin='8,4,8,6' Background='#FFF9F3' BorderBrush='#ECCFBD' BorderThickness='1' CornerRadius='17' Padding='12,7' Visibility='Hidden'>
<TextBlock x:Name='BubbleText' Foreground='#71574C' TextWrapping='Wrap' TextAlignment='Center' VerticalAlignment='Center' FontSize='12'/></Border>
<Image x:Name='Pet' Grid.Row='1' Width='220' Height='220' Stretch='Uniform' RenderTransformOrigin='0.5,0.9' Cursor='Hand'/>
<Border Grid.Row='2' Background='#F7FFF9F5' BorderBrush='#E4CABA' BorderThickness='1' CornerRadius='17' Margin='5,0'>
<Grid Margin='10,3'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
<StackPanel><TextBlock x:Name='Phase' FontSize='10' Foreground='#88756B'/><TextBlock x:Name='Clock' FontSize='20' FontWeight='SemiBold' Foreground='#604D45'/></StackPanel>
<StackPanel Grid.Column='1' Orientation='Horizontal' VerticalAlignment='Center'>
<Button x:Name='Pause' Content='Ⅱ' ToolTip='暂停 / 继续' Width='28' Height='28' Margin='2' Background='#FFF9F3' BorderBrush='#E4CABA'/>
<Button x:Name='Settings' Content='⚙' ToolTip='设置' Width='28' Height='28' Margin='2' Background='#FFF9F3' BorderBrush='#E4CABA'/>
<Button x:Name='Hide' Content='−' ToolTip='收到托盘' Width='24' Height='28' Margin='2' Background='#FFF9F3' BorderBrush='#E4CABA'/>
</StackPanel></Grid></Border></Grid>";
   var grid=(Grid)XamlReader.Parse(layout);Content=new Viewbox{Child=grid,Stretch=Stretch.Uniform};
   pet=(Image)grid.FindName("Pet");clock=(TextBlock)grid.FindName("Clock");phase=(TextBlock)grid.FindName("Phase");bubble=(Border)grid.FindName("Bubble");bubbleText=(TextBlock)grid.FindName("BubbleText");pauseButton=(Button)grid.FindName("Pause");
   var transforms=new TransformGroup();transforms.Children.Add(petBreath);transforms.Children.Add(petOffset);pet.RenderTransform=transforms;
   var image=new BitmapImage();using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("PiggyBuddy.pet.png")){
    if(stream==null)throw new FileNotFoundException("桌宠素材缺失");image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
   }
   int half=image.PixelWidth/2;idleSprite=new CroppedBitmap(image,new Int32Rect(0,0,half,image.PixelHeight));idleSprite.Freeze();alertSprite=new CroppedBitmap(image,new Int32Rect(half,0,half,image.PixelHeight));alertSprite.Freeze();pet.Source=idleSprite;
   pauseButton.Click+=delegate{TogglePause();};((Button)grid.FindName("Settings")).Click+=delegate{OpenSettings();};((Button)grid.FindName("Hide")).Click+=delegate{Hide();};
   pet.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
    if(e.ClickCount==2){OpenSettings();e.Handled=true;return;}
    var before=new Point(Left,Top);try{DragMove();}catch(InvalidOperationException){}
    ClampPosition();SavePosition();
    if(Math.Abs(Left-before.X)+Math.Abs(Top-before.Y)<3)Say(core.Paused?"休息一下也很好。点击 ▶ 继续。":"我和小猪陪着你，记得起来活动哦。",5);
    e.Handled=true;
   };
   var context=new ContextMenu();MenuItem item;
   item=new MenuItem{Header="设置与提醒方式"};item.Click+=delegate{OpenSettings();};context.Items.Add(item);
   item=new MenuItem{Header="暂停 / 继续"};item.Click+=delegate{TogglePause();};context.Items.Add(item);
   item=new MenuItem{Header="重新开始一轮"};item.Click+=delegate{Restart();};context.Items.Add(item);
   item=new MenuItem{Header="稍后 5 分钟提醒"};item.Click+=delegate{Snooze();};context.Items.Add(item);
   item=new MenuItem{Header="隐藏到托盘"};item.Click+=delegate{Hide();};context.Items.Add(item);
   context.Items.Add(new Separator());item=new MenuItem{Header="退出"};item.Click+=delegate{Close();};context.Items.Add(item);ContextMenu=context;
   tray=new Forms.NotifyIcon();tray.Icon=System.Drawing.SystemIcons.Information;tray.Text="抱猪提醒";tray.Visible=true;
   var menu=new Forms.ContextMenuStrip();menu.Items.Add("显示桌宠",null,delegate{ShowPet();});pauseMenu=(Forms.ToolStripMenuItem)menu.Items.Add("暂停",null,delegate{TogglePause();});
   menu.Items.Add("设置",null,delegate{OpenSettings();});menu.Items.Add("重新开始一轮",null,delegate{Restart();});menu.Items.Add("退出",null,delegate{Close();});tray.ContextMenuStrip=menu;
   tray.DoubleClick+=delegate{ShowPet();};tray.BalloonTipClicked+=delegate{ShowPet();};
   ApplySize();var area=SystemParameters.WorkArea;Left=cfg.Left==-9999?area.Right-Width-22:cfg.Left;Top=cfg.Top==-9999?area.Bottom-Height-16:cfg.Top;
   Loaded+=delegate{ClampPosition();Say("你好，我来提醒你起来活动。\n点击 ⚙ 设置时间和提醒方式。",9);};
   Closing+=delegate{SavePosition();if(ticker!=null)ticker.Stop();CloseReminder();if(settingsWindow!=null)settingsWindow.Close();if(speech!=null)speech.Dispose();if(sound!=null)sound.Dispose();tray.Visible=false;tray.Dispose();};
   watch.Start();lastDay=DateTime.Today.DayOfYear;
   ticker=new DispatcherTimer();ticker.Interval=TimeSpan.FromMilliseconds(80);ticker.Tick+=delegate{Tick();};ticker.Start();Render();
  }
  void ApplySize(){Width=260*cfg.Scale/100.0;Height=350*cfg.Scale/100.0;Topmost=cfg.Topmost;}
  void ClampPosition(){
   // WPF coordinates are device-independent, including on high-DPI displays.
   double x=SystemParameters.VirtualScreenLeft,y=SystemParameters.VirtualScreenTop;
   Left=Math.Max(x,Math.Min(Left,x+SystemParameters.VirtualScreenWidth-Width));Top=Math.Max(y,Math.Min(Top,y+SystemParameters.VirtualScreenHeight-Height));
  }
  void SavePosition(){cfg.Left=Left;cfg.Top=Top;Persist();}
  void Persist(){try{cfg.Save();}catch(Exception ex){Entry.Log(ex);}}
  void ShowPet(){Show();ClampPosition();}
  void Say(string msg,double seconds){bubbleText.Text=msg;bubble.Visibility=Visibility.Visible;bubbleUntil=watch.Elapsed.TotalSeconds+seconds;}
  void ClearAlerts(){previewUntil=0;jumpUntil=0;bubbleUntil=0;bubble.Visibility=Visibility.Hidden;if(speech!=null)speech.SpeakAsyncCancelAll();if(sound!=null)sound.Stop();CloseReminder();}
  void TogglePause(){core.Paused=!core.Paused;cfg.Paused=core.Paused;lastTime=watch.Elapsed.TotalSeconds;ClearAlerts();Persist();Render();}
  void Restart(){ClearAlerts();core.Reset();core.Paused=false;cfg.Paused=false;lastTime=watch.Elapsed.TotalSeconds;Persist();Render();}
  void Snooze(){ClearAlerts();core.Snooze();cfg.Paused=false;lastTime=watch.Elapsed.TotalSeconds;Persist();Say("好，5 分钟后再提醒你。",5);Render();}
  void Tick(){
   double now=watch.Elapsed.TotalSeconds,elapsed=now-lastTime;lastTime=now;
   bool wasAllowed=allowed;DateTime local=DateTime.Now;allowed=TimerCore.IsAllowed(cfg.Daily,cfg.StartMinute,cfg.EndMinute,local.Hour*60+local.Minute);
   if(wasAllowed && !allowed)ClearAlerts();
   if(local.DayOfYear!=lastDay){lastDay=local.DayOfYear;core.Completed=0;}
   Transition t=core.Advance(elapsed,allowed);
   if(t==Transition.RestStarted)Notify(cfg,true,false);
   else if(t==Transition.WorkStarted){CloseReminder();if(cfg.EndAlert)Notify(cfg,false,false);else ClearAlerts();}
   double offset=0,scale=1;
   if(((allowed && !core.Paused) || now<previewUntil) && IsVisible){scale=1+0.008*Math.Sin(now*1.8);if(now<jumpUntil)offset=-9*Math.Abs(Math.Sin(now*4.5));}
   petOffset.Y=offset;petBreath.ScaleX=scale;petBreath.ScaleY=scale;
   bool alert=now<jumpUntil;pet.Source=alert?alertSprite:idleSprite;
   if(now>bubbleUntil)bubble.Visibility=Visibility.Hidden;
   Render();
  }
  void Render(){
   int secs=(int)Math.Ceiling(Math.Max(0,core.Remaining));clock.Text=string.Format("{0:00}:{1:00}",secs/60,secs%60);
   phase.Text=core.Paused?"已暂停":!allowed?"休息时段 · 计时挂起":core.IsRest?"活动中 · 完成后自动下一轮":"专注中 · 今日完成 "+core.Completed+" 轮";
   pauseButton.Content=core.Paused?"▶":"Ⅱ";pauseMenu.Text=core.Paused?"继续":"暂停";
   tray.Text="抱猪提醒 · "+phase.Text+" "+clock.Text;
  }
  void Notify(Settings options,bool isRest,bool preview){
   ClearAlerts();if(preview)previewUntil=watch.Elapsed.TotalSeconds+14;
   string message=isRest?options.Message:"活动结束，已自动开始下一轮。";
   if(preview)message="提醒预览："+message;
   if(options.Bubble || options.Jump){ShowPet();if(options.Bubble)Say(message,14);if(options.Jump)jumpUntil=watch.Elapsed.TotalSeconds+12;}
   if(options.Balloon){tray.BalloonTipTitle=preview?"提醒预览":isRest?"起来活动一下":"新一轮开始";tray.BalloonTipText=message;tray.ShowBalloonTip(8000);}
   if(options.Sound){
    try{if(!string.IsNullOrWhiteSpace(options.Wav) && File.Exists(options.Wav)){if(sound!=null)sound.Dispose();sound=new SoundPlayer(options.Wav);sound.Play();}else SystemSounds.Exclamation.Play();}
    catch(Exception ex){Entry.Log(ex);SystemSounds.Exclamation.Play();}
   }
   if(options.Voice){
    try{
     if(speech==null){speech=new SpeechSynthesizer();speech.SetOutputToDefaultAudioDevice();bool chinese=false;foreach(var voice in speech.GetInstalledVoices()){if(voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName=="zh"){speech.SelectVoice(voice.VoiceInfo.Name);chinese=true;break;}}if(!chinese)throw new InvalidOperationException("未找到中文语音，请在 Windows 语言设置中安装中文语音。 ");}
     speech.SpeakAsyncCancelAll();speech.Volume=options.Volume;speech.SpeakAsync(message);speechStatus="";
    }catch(Exception ex){if(speech!=null){speech.Dispose();speech=null;}Entry.Log(ex);speechStatus=ex.Message;if(preview)MessageBox.Show(this,"语音预览失败："+speechStatus,"中文语音提示");}
   }
   if(options.Popup)ShowReminder(message,preview,isRest);
  }
  void CloseReminder(){if(reminderWindow!=null){var w=reminderWindow;reminderWindow=null;w.Close();}}
  void ShowReminder(string message,bool preview,bool isRest){
   CloseReminder();var w=new Window{Title=preview?"提醒预览":"抱猪提醒",Width=430,Height=280,ResizeMode=ResizeMode.NoResize,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Brush("#FFF9F3"),FontFamily=FontFamily};reminderWindow=w;
   var stack=new StackPanel{Margin=new Thickness(25)};w.Content=stack;
   stack.Children.Add(new TextBlock{Text=isRest?"起来活动一下吧":"新的专注时间",FontSize=24,Foreground=Brush("#765449"),FontWeight=FontWeights.SemiBold});
   stack.Children.Add(new TextBlock{Text=message,FontSize=15,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,20)});
   var row=new StackPanel{Orientation=Orientation.Horizontal};stack.Children.Add(row);
   var ok=Btn(preview?"关闭预览":"知道啦",delegate{w.Close();});row.Children.Add(ok);
   if(!preview && isRest)row.Children.Add(Btn("稍后 5 分钟",delegate{Snooze();}));
   stack.Children.Add(new TextBlock{Text=preview?"这是预览，不改变当前计时。":"计时会自动循环，关闭提醒不影响计时。",FontSize=11,Foreground=Brush("#927C70"),Margin=new Thickness(0,15,0,0)});
   w.Closed+=delegate{if(reminderWindow==w)reminderWindow=null;};w.Show();
  }
  static Brush Brush(string hex){return (Brush)new BrushConverter().ConvertFromString(hex);}
  static Button Btn(string label,Action action){var b=new Button{Content=label,Padding=new Thickness(13,7,13,7),Margin=new Thickness(0,3,8,3),Background=Brush("#F4E1D4"),BorderBrush=Brush("#E1C9B9"),Foreground=Brush("#634D43")};b.Click+=delegate{action();};return b;}
  static TextBlock Label(string text,int size){return new TextBlock{Text=text,FontSize=size,Foreground=Brush("#634D43"),Margin=new Thickness(0,8,0,7),TextWrapping=TextWrapping.Wrap};}
  static CheckBox Check(string text,bool value){return new CheckBox{Content=text,IsChecked=value,Margin=new Thickness(0,6,18,6),Foreground=Brush("#634D43"),FontSize=14};}
  static TextBox Field(string text,int width){return new TextBox{Text=text,Width=width,Padding=new Thickness(7),Margin=new Thickness(0,2,10,2),VerticalContentAlignment=VerticalAlignment.Center};}
  static bool Yes(CheckBox c){return c.IsChecked==true;}
  static string TimeText(int minute){return string.Format("{0:00}:{1:00}",minute/60,minute%60);}
  static int ParseTime(string text){TimeSpan v;if(!TimeSpan.TryParseExact(text.Trim(),@"hh\:mm",System.Globalization.CultureInfo.InvariantCulture,out v) || v.TotalHours>=24)throw new Exception("时段请填写 24 小时制，例如 09:00、22:00。");return (int)v.TotalMinutes;}
  void OpenSettings(){
   if(settingsWindow!=null){settingsWindow.Activate();return;}
   var w=new Window{Title="抱猪提醒 · 设置",Width=590,Height=Math.Min(750,SystemParameters.WorkArea.Height-35),MinWidth=530,MinHeight=440,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Brush("#FFFAF6"),FontFamily=FontFamily};settingsWindow=w;
   var root=new DockPanel{Margin=new Thickness(24)};w.Content=root;
   var heading=new StackPanel();heading.Children.Add(Label("和小猪一起，记得起来活动",23));heading.Children.Add(Label("自动循环  ·  提醒自由组合  ·  离线运行",12));DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
   var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
   var error=Label("",12);error.Foreground=Brush("#AF4747");bottom.Children.Add(error);
   var actions=new StackPanel{Orientation=Orientation.Horizontal};bottom.Children.Add(actions);
   var tabs=new TabControl{Background=Brushes.Transparent,BorderBrush=Brush("#E8D7CA")};root.Children.Add(tabs);
   Func<string,StackPanel> tab=delegate(string name){var panel=new StackPanel{Margin=new Thickness(15)};tabs.Items.Add(new TabItem{Header=name,Padding=new Thickness(12,8,12,8),Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});return panel;};
   var timing=tab("计时");timing.Children.Add(Label("循环节奏",18));
   timing.Children.Add(Label("工作时长（1–240 分钟）",13));var work=Field(cfg.Work.ToString(),110);work.HorizontalAlignment=HorizontalAlignment.Left;timing.Children.Add(work);
   timing.Children.Add(Label("活动时长（1–60 分钟）",13));var rest=Field(cfg.Rest.ToString(),110);rest.HorizontalAlignment=HorizontalAlignment.Left;timing.Children.Add(rest);
   timing.Children.Add(Label("工作 → 活动 → 下一轮，无需每轮手动开启。",12));
   var endAlert=Check("活动结束时也提醒我",cfg.EndAlert);timing.Children.Add(endAlert);
   var daily=Check("仅在以下时段自动计时和提醒",cfg.Daily);timing.Children.Add(daily);
   var times=new StackPanel{Orientation=Orientation.Horizontal};var begin=Field(TimeText(cfg.StartMinute),100);var end=Field(TimeText(cfg.EndMinute),100);times.Children.Add(begin);times.Children.Add(Label("至   ",14));times.Children.Add(end);timing.Children.Add(times);
   timing.Children.Add(Label("支持跨午夜，例如 22:00–06:00。相同时间表示全天。\n时段外挂起倒计时，回到时段后继续；睡眠后重新开始一轮。",12));
   timing.Children.Add(Btn("立即重新开始一轮",delegate{Restart();error.Text="已重新开始当前设置下的一轮。未保存的编辑尚未应用。";}));
   var alerts=tab("提醒方式");alerts.Children.Add(Label("可以同时选择多项",18));
   var soundCheck=Check("提示音",cfg.Sound);var voiceCheck=Check("中文语音播报",cfg.Voice);var balloonCheck=Check("Windows 托盘气泡",cfg.Balloon);var bubbleCheck=Check("桌宠对话气泡",cfg.Bubble);var jumpCheck=Check("桌宠挥手＋轻轻跳动",cfg.Jump);var popupCheck=Check("独立提醒窗口（置顶）",cfg.Popup);
   alerts.Children.Add(soundCheck);alerts.Children.Add(voiceCheck);alerts.Children.Add(balloonCheck);alerts.Children.Add(bubbleCheck);alerts.Children.Add(jumpCheck);alerts.Children.Add(popupCheck);
   alerts.Children.Add(Label("提醒内容",13));var msg=Field(cfg.Message,445);msg.MaxLength=120;msg.TextWrapping=TextWrapping.Wrap;msg.AcceptsReturn=false;msg.Height=66;alerts.Children.Add(msg);
   alerts.Children.Add(Label("语音音量（提示音使用系统音量）",12));var volume=new Slider{Minimum=0,Maximum=100,Value=cfg.Volume,TickFrequency=10,IsSnapToTickEnabled=true,Width=300,HorizontalAlignment=HorizontalAlignment.Left};alerts.Children.Add(volume);
   alerts.Children.Add(Label("自定义 WAV 提示音（留空使用系统提示音）",12));var wav=Field(cfg.Wav,445);alerts.Children.Add(wav);
   var fileRow=new StackPanel{Orientation=Orientation.Horizontal};fileRow.Children.Add(Btn("选择 WAV",delegate{var dialog=new OpenFileDialog{Filter="WAV 音频|*.wav",CheckFileExists=true};if(dialog.ShowDialog(w)==true)wav.Text=dialog.FileName;}));fileRow.Children.Add(Btn("恢复默认音效",delegate{wav.Text="";}));alerts.Children.Add(fileRow);
   alerts.Children.Add(Label("Windows 专注助手可能隐藏托盘气泡。中文播报需要系统已安装中文语音。",12));
   var general=tab("桌宠与启动");general.Children.Add(Label("桌宠大小",18));
   var scale=new Slider{Minimum=65,Maximum=150,Value=cfg.Scale,TickFrequency=5,IsSnapToTickEnabled=true,Width=360,HorizontalAlignment=HorizontalAlignment.Left};general.Children.Add(scale);
   var sizeLabel=Label(cfg.Scale+"%",13);general.Children.Add(sizeLabel);scale.ValueChanged+=delegate{sizeLabel.Text=((int)scale.Value)+"%";};
   var top=Check("桌宠始终置顶",cfg.Topmost);general.Children.Add(top);
   var auto=Check("登录 Windows 后自动启动",StartupEnabled());general.Children.Add(auto);
   general.Children.Add(Label("拖动人物移动桌宠，双击人物打开设置。\n右键人物可暂停、重开、延后提醒或退出。\n点击 − 收到托盘；双击托盘图标找回。",13));
   general.Children.Add(Btn("找回桌宠到主屏幕",delegate{var area=SystemParameters.WorkArea;Left=area.Right-Width-22;Top=area.Bottom-Height-16;ShowPet();SavePosition();}));
   general.Children.Add(Label("计时用单调时钟，修改系统时间不影响单轮时长。\n关闭设置不会暂停。更改工作或活动时长并保存，会重开一轮。\n本版本不联网，不上传照片或使用记录。",12));
   Func<Settings> read=delegate{
    int wm,rm;if(!int.TryParse(work.Text,out wm)||wm<1||wm>240||!int.TryParse(rest.Text,out rm)||rm<1||rm>60)throw new Exception("工作填 1–240、活动填 1–60 的整数分钟。");
    if(!Yes(soundCheck)&&!Yes(voiceCheck)&&!Yes(balloonCheck)&&!Yes(bubbleCheck)&&!Yes(jumpCheck)&&!Yes(popupCheck))throw new Exception("请至少选择一种提醒方式。");
    if(string.IsNullOrWhiteSpace(msg.Text))throw new Exception("请填写提醒内容。");
    if(!string.IsNullOrWhiteSpace(wav.Text) && (!File.Exists(wav.Text.Trim()) || !string.Equals(Path.GetExtension(wav.Text.Trim()),".wav",StringComparison.OrdinalIgnoreCase)))throw new Exception("请选择存在的 WAV 文件，或清空使用默认提示音。");
    return new Settings{Work=wm,Rest=rm,Sound=Yes(soundCheck),Voice=Yes(voiceCheck),Balloon=Yes(balloonCheck),Bubble=Yes(bubbleCheck),Jump=Yes(jumpCheck),Popup=Yes(popupCheck),Message=msg.Text.Trim(),Wav=wav.Text.Trim(),Volume=(int)volume.Value,Scale=(int)scale.Value,Topmost=Yes(top),AutoStart=Yes(auto),Daily=Yes(daily),StartMinute=ParseTime(begin.Text),EndMinute=ParseTime(end.Text),Paused=core.Paused,EndAlert=Yes(endAlert),Left=Left,Top=Top};
   };
   actions.Children.Add(Btn("预览提醒",delegate{try{error.Text="";ClearAlerts();Notify(read(),true,true);}catch(Exception ex){error.Text=ex.Message;}}));
   actions.Children.Add(Btn("保存设置",delegate{
    try{
     Settings next=read();bool oldStartup=StartupEnabled();
     SetStartup(next.AutoStart);
     try{next.Save();}catch{SetStartup(oldStartup);throw;}
     bool reset=next.Work!=cfg.Work||next.Rest!=cfg.Rest;cfg=next;core.WorkSeconds=cfg.Work*60;core.RestSeconds=cfg.Rest*60;
     if(reset){core.Reset();lastTime=watch.Elapsed.TotalSeconds;}ClearAlerts();ApplySize();ClampPosition();Render();w.Close();
    }catch(Exception ex){error.Text="保存失败："+ex.Message;Entry.Log(ex);}
   }));
   actions.Children.Add(Btn("取消",delegate{w.Close();}));
   w.Closed+=delegate{settingsWindow=null;};w.Show();
  }
  static bool StartupEnabled(){try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){return key!=null && key.GetValue("PiggyBuddy")!=null;}}catch{return false;}}
  static void SetStartup(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(enabled)key.SetValue("PiggyBuddy","\""+Assembly.GetExecutingAssembly().Location+"\"");else key.DeleteValue("PiggyBuddy",false);}}
 }
}
