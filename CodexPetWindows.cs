using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexPetWin7 {
static class CrashLog {
    public static string PathName { get { string d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexPetWin7");Directory.CreateDirectory(d);return Path.Combine(d,"crash.log"); } }
    public static void Write(Exception ex){try{File.AppendAllText(PathName,"\r\n["+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+"]\r\n"+ex+"\r\n",Encoding.UTF8);}catch{}}
}
static class Native {
    public const int WH_KEYBOARD_LL=13, WH_MOUSE_LL=14;
    public const uint LLMHF_INJECTED=1, LLKHF_INJECTED=0x10, MOUSEEVENTF_MOVE=1;
    public delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct MData { public Point pt; public uint mouseData,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KData { public uint vk,scan,flags,time; public UIntPtr extra; }
    [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int id,HookProc p,IntPtr mod,uint tid);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h,int n,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string n);
    [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd,out RECT rect);
}

sealed class Word {
    public string Book,Text,Phonetic,Meaning,Example;
    public string Key { get { return Book+":"+Text.ToLowerInvariant(); } }
}

sealed class MemoryState {
    public double Stability=0.05,Difficulty=5; public DateTime Due=DateTime.UtcNow,Last=DateTime.UtcNow; public int Ok,Bad;
}

sealed class Theme {
    public static readonly Color Bg=Color.FromArgb(246,248,252), Card=Color.White, Ink=Color.FromArgb(28,34,49), Muted=Color.FromArgb(96,105,124), Blue=Color.FromArgb(54,106,255), Pale=Color.FromArgb(231,237,255), Green=Color.FromArgb(35,156,107), Orange=Color.FromArgb(232,139,45), Red=Color.FromArgb(215,75,75);
    public static Button Button(string text,int x,int y,int w,int h,Color color){Button b=new Button();b.Text=text;b.SetBounds(x,y,w,h);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=color;b.ForeColor=Color.White;b.Font=new Font("Microsoft YaHei UI",9,FontStyle.Bold);return b;}
    public static Label Label(string text,int x,int y,int w,int h,float size,bool bold){Label l=new Label();l.Text=text;l.SetBounds(x,y,w,h);l.ForeColor=Ink;l.Font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);return l;}
}

class BubbleForm:Form {
    protected readonly Color Bubble=Color.FromArgb(31,36,49), Line=Color.FromArgb(75,84,107), Light=Color.FromArgb(242,245,255), Subtle=Color.FromArgb(171,181,205);
    public bool SilentShow;
    protected override bool ShowWithoutActivation { get { return SilentShow; } }
    public BubbleForm(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;TopMost=true;BackColor=Bubble;Opacity=.92;Font=new Font("Microsoft YaHei UI",9);DoubleBuffered=true;}
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<30||Height<30)return;GraphicsPath p=RoundPath(new Rectangle(0,0,Width,Height),12);Region old=Region;Region=new Region(p);p.Dispose();if(old!=null)old.Dispose();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(Pen p=new Pen(Line,1))using(GraphicsPath q=RoundPath(new Rectangle(0,0,ClientSize.Width-1,ClientSize.Height-1),12))e.Graphics.DrawPath(p,q);}
    static GraphicsPath RoundPath(Rectangle r,int z){GraphicsPath p=new GraphicsPath();int d=z*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    protected Label TextLabel(string text,int x,int y,int w,int h,float size,bool bold,Color color){Label l=Theme.Label(text,x,y,w,h,size,bold);l.ForeColor=color;l.BackColor=Bubble;return l;}
    protected Button SmallButton(string text,int x,int y,int w,int h,Color color){Button b=Theme.Button(text,x,y,w,h,color);b.TabStop=false;return b;}
    public void Flash(){BringToFront();Activate();Opacity=.72;System.Windows.Forms.Timer t=new System.Windows.Forms.Timer();t.Interval=140;t.Tick+=delegate{Opacity=1;t.Stop();t.Dispose();};t.Start();}
}

sealed class WordCardForm:BubbleForm {
    readonly Word word; readonly Action<int,bool> grade; readonly bool autoSpeak; readonly Label title,phonetic,meaning; readonly Button known,unknown,sound,reveal; bool revealed,committed,corrected; int pending=-1; System.Windows.Forms.Timer feedbackTimer;
    public WordCardForm(Word w,bool speakOn,bool silent,Action<int,bool> onGrade){word=w;grade=onGrade;autoSpeak=speakOn;SilentShow=silent;Text="Codex 单词提示";ClientSize=new Size(286,116);KeyPreview=true;
        title=TextLabel(w.Text,14,9,150,29,17,true,Light);title.Cursor=Cursors.Hand;title.Click+=delegate{Speak();};Controls.Add(title);
        phonetic=TextLabel(w.Phonetic.Length>0?w.Phonetic:"点击发音",14,36,190,22,9,false,Color.FromArgb(142,177,255));phonetic.Cursor=Cursors.Hand;phonetic.Click+=delegate{Speak();};Controls.Add(phonetic);
        sound=SmallButton("▶",205,12,30,27,Theme.Blue);sound.Click+=delegate{Speak();};Controls.Add(sound);reveal=SmallButton("释",241,12,30,27,Color.FromArgb(83,91,111));reveal.Click+=delegate{ToggleMeaning();};Controls.Add(reveal);
        meaning=TextLabel(Short(w.Meaning,34),14,59,255,22,9,false,Light);meaning.Visible=false;Controls.Add(meaning);
        unknown=SmallButton("不认识",48,84,86,25,Color.FromArgb(83,91,111));unknown.Click+=delegate{Submit(false);};Controls.Add(unknown);known=SmallButton("认识",151,84,86,25,Theme.Blue);known.Click+=delegate{Submit(true);};Controls.Add(known);
        KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Escape)Close();};Shown+=delegate{if(autoSpeak)Speak();};
    }
    void ToggleMeaning(){if(pending>=0)return;revealed=!revealed;meaning.Visible=revealed;reveal.Text=revealed?"隐":"释";}
    void Submit(bool yes){int choice=yes?2:0;if(pending>=0){if(choice!=pending)corrected=true;pending=choice;ShowChoice();return;}if(revealed){Commit(choice,true);Close();return;}pending=choice;revealed=true;meaning.Visible=true;reveal.Text="释";reveal.Enabled=false;ShowChoice();feedbackTimer=new System.Windows.Forms.Timer();feedbackTimer.Interval=4000;feedbackTimer.Tick+=delegate{feedbackTimer.Stop();Commit(pending,corrected);Close();};feedbackTimer.Start();}
    void ShowChoice(){known.BackColor=pending==2?Theme.Green:Color.FromArgb(83,91,111);unknown.BackColor=pending==0?Theme.Red:Color.FromArgb(83,91,111);}
    void Commit(int choice,bool wasRevealed){if(committed)return;committed=true;grade(choice,wasRevealed);}
    protected override void OnFormClosing(FormClosingEventArgs e){if(pending>=0&&!committed)Commit(pending,corrected);if(feedbackTimer!=null){feedbackTimer.Stop();feedbackTimer.Dispose();feedbackTimer=null;}base.OnFormClosing(e);}
    static string Short(string s,int n){return s.Length<=n?s:s.Substring(0,n-1)+"…";}
    void Speak(){try{using(SpeechSynthesizer s=new SpeechSynthesizer()){s.Rate=-1;s.SpeakAsync(word.Text);}}catch{}}
}

sealed class WaterForm:BubbleForm {
    public WaterForm(Action drank,Action snooze){Text="喝水气泡";ClientSize=new Size(270,92);
        Controls.Add(TextLabel("该喝水啦",14,8,118,27,14,true,Light));Controls.Add(TextLabel("喝口水，顺便眺望一下",14,35,240,20,8.5f,false,Subtle));
        Button yes=SmallButton("喝过了",43,61,82,24,Theme.Green);yes.Click+=delegate{drank();Close();};Controls.Add(yes);Button later=SmallButton("稍后",145,61,82,24,Theme.Blue);later.Click+=delegate{snooze();Close();};Controls.Add(later);
    }
}

sealed class NoticeForm:BubbleForm {
    public NoticeForm(string title,string text,Action acknowledged){Text="Codex 提醒气泡";ClientSize=new Size(270,96);Controls.Add(TextLabel(title,14,8,235,27,14,true,Light));Controls.Add(TextLabel(text,14,34,240,27,8.5f,false,Subtle));Button ok=SmallButton("我还在使用",79,65,112,24,Theme.Blue);ok.Click+=delegate{acknowledged();Close();};Controls.Add(ok);}
}

sealed class SettingsForm:Form {
    public readonly NumericUpDown Idle=new NumericUpDown(),Pulse=new NumericUpDown(),DailyNew=new NumericUpDown(),AutoWordMinutes=new NumericUpDown(),WaterMinutes=new NumericUpDown(),Snooze=new NumericUpDown(),PetScale=new NumericUpDown();
    public readonly CheckBox Awake=new CheckBox(),AutoSpeak=new CheckBox(),AutoWordEnabled=new CheckBox(),PauseFullscreen=new CheckBox(),WaterEnabled=new CheckBox(),EyeFollow=new CheckBox(),AutoStartBox=new CheckBox(); public readonly new CheckBox Top=new CheckBox();
    public readonly ComboBox BookMode=new ComboBox();
    public SettingsForm(int idle,int pulse,int dailyNew,int autoWordMinutes,int waterMinutes,int snooze,int petScale,bool awake,bool autoSpeak,bool autoWordEnabled,bool pauseFullscreen,bool waterEnabled,bool top,bool eyeFollow,bool autoStart,string book){
        Text="Codex 桌宠设置";ClientSize=new Size(520,430);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;BackColor=Theme.Bg;Font=new Font("Microsoft YaHei UI",9);
        TabControl tabs=new TabControl();tabs.SetBounds(14,14,492,355);Controls.Add(tabs);
        TabPage lockTab=Page(tabs,"防锁屏"),wordTab=Page(tabs,"单词学习"),waterTab=Page(tabs,"喝水提醒"),petTab=Page(tabs,"桌面宠物"),generalTab=Page(tabs,"通用");
        Check(Awake,"启用防锁屏",22,24,awake,lockTab);Num(lockTab,"真实无人操作后锁屏（分钟）",Idle,22,68,10,480,10,idle);Num(lockTab,"模拟输入间隔（分钟）",Pulse,22,112,1,9,1,pulse);Info(lockTab,"模拟输入只阻止系统提前锁屏；达到真实无人操作时间后仍会主动锁屏。",22,165,430);
        Check(AutoWordEnabled,"自动弹出单词提示",22,20,autoWordEnabled,wordTab);Num(wordTab,"自动提醒间隔（分钟）",AutoWordMinutes,22,62,5,240,5,autoWordMinutes);Num(wordTab,"每日新词数量",DailyNew,22,106,0,100,5,dailyNew);Combo(wordTab,"词库范围",BookMode,22,150,new string[]{"全部词库","IELTS","日常高频"},book);Check(AutoSpeak,"出现单词时自动发音",22,197,autoSpeak,wordTab);Check(PauseFullscreen,"锁屏或全屏时暂停自动提醒",250,197,pauseFullscreen,wordTab);Info(wordTab,"自动提醒每天不限次数：先安排到期复习，再按每日新词量出新词。双击宠物也可随时背词。",22,245,430);
        Check(WaterEnabled,"启用喝水提醒",22,24,waterEnabled,waterTab);Num(waterTab,"提醒间隔（分钟）",WaterMinutes,22,68,15,240,15,waterMinutes);Num(waterTab,"稍后提醒（分钟）",Snooze,22,112,5,60,5,snooze);Info(waterTab,"电脑锁屏时暂停提醒，解锁后重新开始计时；提醒卡必须手动处理。",22,165,430);
        Num(petTab,"宠物大小（百分比）",PetScale,22,24,60,180,10,petScale);Check(Top,"始终置顶",22,76,top,petTab);Check(EyeFollow,"鼠标靠近时跟随视线",250,76,eyeFollow,petTab);Info(petTab,"单击互动，双击背词，按住拖动；宠物本身不提供右键菜单。",22,125,430);
        Check(AutoStartBox,"登录 Windows 后自动运行",22,28,autoStart,generalTab);Info(generalTab,"设置和退出统一放在系统托盘区域。所有配置与学习进度按当前 Windows 用户保存。",22,78,430);
        Button ok=Theme.Button("保存",164,384,88,32,Theme.Blue);ok.DialogResult=DialogResult.OK;Controls.Add(ok);Button no=Theme.Button("取消",270,384,88,32,Theme.Muted);no.DialogResult=DialogResult.Cancel;Controls.Add(no);AcceptButton=ok;CancelButton=no;
    }
    static TabPage Page(TabControl t,string name){TabPage p=new TabPage(name);p.BackColor=Theme.Card;t.TabPages.Add(p);return p;}
    static void Check(CheckBox c,string text,int x,int y,bool value,Control p){c.Text=text;c.SetBounds(x,y,210,28);c.Checked=value;p.Controls.Add(c);}
    static void Num(Control p,string text,NumericUpDown n,int x,int y,int min,int max,int inc,int value){p.Controls.Add(Theme.Label(text,x,y,275,28,9,false));n.SetBounds(330,y-2,90,26);n.Minimum=min;n.Maximum=max;n.Increment=inc;n.Value=Math.Max(min,Math.Min(max,value));p.Controls.Add(n);}
    static void Combo(Control p,string text,ComboBox c,int x,int y,string[] items,string value){p.Controls.Add(Theme.Label(text,x,y,180,28,9,false));c.SetBounds(220,y-3,200,27);c.DropDownStyle=ComboBoxStyle.DropDownList;c.Items.AddRange(items);c.SelectedItem=value;if(c.SelectedIndex<0)c.SelectedIndex=0;p.Controls.Add(c);}
    static void Info(Control p,string text,int x,int y,int w){Label l=Theme.Label(text,x,y,w,55,9,false);l.ForeColor=Theme.Muted;p.Controls.Add(l);}
}

sealed class PetForm:Form {
    const string Reg=@"Software\CodexPetWin7"; readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer(); readonly NotifyIcon tray=new NotifyIcon(); readonly Random rnd=new Random();
    Image atlas;readonly List<Word> ielts=new List<Word>(),daily=new List<Word>();DateTime realInput=DateTime.UtcNow,lastPulse=DateTime.UtcNow,nextWater,nextAutoWord,statusUntil,actionUntil;uint lastInputTick;string action="idle",footStatus="";int frame,actionFrame,idleMin,pulseMin,dailyNew,autoWordMinutes,waterMinutes,snoozeMinutes,petScale,petSide;bool awake,locked,warned,dragging,top,eyeFollow,autoSpeak,autoWordEnabled,pauseFullscreen,waterEnabled,pendingWater,pendingWord;Point dragAt;WordCardForm wordCard;WaterForm waterCard;NoticeForm lockCard;
    public PetForm(){LoadSettings();Text="Codex 桌宠";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;BackColor=Color.Fuchsia;TransparencyKey=Color.Fuchsia;DoubleBuffered=true;StartPosition=FormStartPosition.Manual;ApplySize();Location=Visible(new Point(Read("X",Screen.PrimaryScreen.WorkingArea.Right-Width,-10000,10000),Read("Y",Screen.PrimaryScreen.WorkingArea.Bottom-Height,-10000,10000)));atlas=Load("CodexPet.atlas.png");LoadWords();BuildTray();
        tray.DoubleClick+=delegate{OpenSettings();};lastInputTick=CurrentInputTick();statusUntil=DateTime.Now.AddSeconds(8);
        MouseDown+=Down;MouseMove+=Move;MouseUp+=Up;MouseDoubleClick+=delegate{OpenWord();};timer.Interval=120;timer.Tick+=SafeTick;timer.Start();Microsoft.Win32.SystemEvents.SessionSwitch+=Session; }
    void LoadSettings(){idleMin=Read("Idle",60,10,480);pulseMin=Read("Pulse",1,1,9);dailyNew=Read("DailyNew",10,0,100);autoWordMinutes=Read("AutoWordMinutes",30,5,240);waterMinutes=Read("WaterMinutes",60,15,240);snoozeMinutes=Read("Snooze",10,5,60);petScale=Read("PetScale",100,60,180);awake=B("Awake",true);top=B("Top",true);eyeFollow=B("EyeFollow",true);autoSpeak=B("AutoSpeak",true);autoWordEnabled=B("AutoWordEnabled",true);pauseFullscreen=B("PauseFullscreen",true);waterEnabled=B("WaterEnabled",true);TopMost=top;nextWater=DateTime.Now.AddMinutes(waterMinutes);nextAutoWord=DateTime.Now.AddMinutes(autoWordMinutes);}
    void ApplySize(){petSide=(int)(210*petScale/100.0);ClientSize=new Size(petSide,petSide+24);}
    void BuildTray(){ContextMenu m=new ContextMenu();m.MenuItems.Add("打开设置…",delegate{OpenSettings();});m.MenuItems.Add("立即背一个单词",delegate{OpenWord();});m.MenuItems.Add("立即喝水提醒",delegate{OpenWater();});m.MenuItems.Add("防锁屏："+(awake?"已开启":"已关闭"),delegate{awake=!awake;Write("Awake",awake?1:0);locked=false;realInput=lastPulse=DateTime.UtcNow;BuildTray();});m.MenuItems.Add("立即锁屏",delegate{Native.LockWorkStation();});m.MenuItems.Add("-");m.MenuItems.Add("退出",delegate{if(MessageBox.Show("确定退出 Codex 桌宠吗？","Codex 桌宠",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){tray.Visible=false;Application.Exit();}});tray.ContextMenu=m;tray.Icon=SystemIcons.Application;tray.Text="Codex 桌宠";tray.Visible=true;}
    void SafeTick(object sender,EventArgs e){try{Tick(sender,e);}catch(Exception ex){CrashLog.Write(ex);timer.Stop();tray.ShowBalloonTip(5000,"Codex 桌宠已暂停","发生异常，错误日志已保存；重新启动程序即可恢复。",ToolTipIcon.Error);}}
    void Tick(object sender,EventArgs e){frame++;PollRealInput();if(awake&&!locked){TimeSpan left=TimeSpan.FromMinutes(idleMin)-(DateTime.UtcNow-realInput);if(left.TotalSeconds<=60&&left.TotalSeconds>0&&!warned){warned=true;SetFoot("即将锁屏",TimeSpan.FromMinutes(1));OpenLockNotice();tray.ShowBalloonTip(3500,"即将锁屏","还剩 1 分钟；真实移动鼠标或按键会重新计时。",ToolTipIcon.Warning);}if(left<=TimeSpan.Zero){locked=true;if(lockCard!=null&&!lockCard.IsDisposed)lockCard.Close();Native.LockWorkStation();}else if((DateTime.UtcNow-lastPulse).TotalMinutes>=pulseMin){Native.mouse_event(1,1,0,0,UIntPtr.Zero);Native.mouse_event(1,unchecked((uint)-1),0,0,UIntPtr.Zero);lastPulse=DateTime.UtcNow;}}
        if(waterEnabled&&!locked&&DateTime.Now>=nextWater&&waterCard==null&&!pendingWater)OpenWater();if(autoWordEnabled&&!locked&&DateTime.Now>=nextAutoWord){if(!pauseFullscreen||!IsFullScreenApp())OpenWord(true);nextAutoWord=DateTime.Now.AddMinutes(autoWordMinutes);}if(DateTime.UtcNow>=actionUntil)action="idle";PositionOpenBubbles();Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(atlas==null)return;Graphics g=e.Graphics;g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;int row=0,col=0;if(action=="wave"){row=3;col=(frame-actionFrame)%4;}else if(action=="jump"){row=4;col=(frame-actionFrame)%5;}else if(action=="happy"){row=8;col=(frame-actionFrame)%6;}else if(action=="think"){row=6;col=(frame-actionFrame)%6;}else if(eyeFollow){Point c=PointToScreen(new Point(petSide/2,petSide/2));Point q=Cursor.Position;double dx=q.X-c.X,dy=q.Y-c.Y,dist=Math.Sqrt(dx*dx+dy*dy);if(dist<260&&dist>45){double deg=(Math.Atan2(dx,-dy)*180/Math.PI+360)%360;int idx=(int)Math.Floor((deg+11.25)/22.5)%16;row=idx<8?9:10;col=idx%8;}}
        int pad=Math.Max(3,(int)(8*petScale/100.0));Rectangle d=new Rectangle(pad,pad,petSide-pad*2,petSide-pad*2);g.DrawImage(atlas,d,col*192,row*208,192,208,GraphicsUnit.Pixel);if(footStatus.Length>0&&DateTime.Now<statusUntil){SizeF z=g.MeasureString(footStatus,Font);int w=(int)z.Width+14,x=(Width-w)/2;using(SolidBrush b=new SolidBrush(Color.FromArgb(31,36,49)))g.FillRectangle(b,x,petSide,w,21);using(SolidBrush b=new SolidBrush(Color.White))g.DrawString(footStatus,Font,b,x+7,petSide+2);}}
    void Down(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left){dragging=false;dragAt=e.Location;Capture=true;}}
    new void Move(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left&&(Math.Abs(e.X-dragAt.X)>4||Math.Abs(e.Y-dragAt.Y)>4)){dragging=true;Location=new Point(Left+e.X-dragAt.X,Top+e.Y-dragAt.Y);PositionOpenBubbles();}}
    void Up(object s,MouseEventArgs e){Capture=false;if(dragging){Location=Visible(Location);Write("X",Left);Write("Y",Top);}else if(e.Button==MouseButtons.Left)Play(rnd.Next(2)==0?"wave":"jump");}
    void Play(string a){action=a;actionFrame=frame;actionUntil=DateTime.UtcNow.AddSeconds(a=="jump"?1.4:1.2);Invalidate();}
    void OpenWord(){OpenWord(false);}
    void OpenWord(bool automatic){if(waterCard!=null&&!waterCard.IsDisposed){pendingWord=true;if(!automatic)waterCard.Flash();SetFoot("先喝水哦",TimeSpan.FromSeconds(4));return;}if(wordCard!=null&&!wordCard.IsDisposed){if(!automatic)wordCard.Flash();return;}Word w=NextWord();if(w==null){SetFoot("词库为空",TimeSpan.FromSeconds(4));return;}MarkSeen(w);wordCard=new WordCardForm(w,autoSpeak,automatic,delegate(int grade,bool revealed){Grade(w,grade,revealed);Play(grade>=2?"happy":"think");});wordCard.FormClosed+=delegate{wordCard=null;SetFoot("",TimeSpan.Zero);if(pendingWater){pendingWater=false;BeginInvoke(new MethodInvoker(OpenWater));}};wordCard.Show(this);PositionBubble(wordCard);SetFoot("",TimeSpan.Zero);Play("wave");}
    Word NextWord(){List<Word> pool=Pool();List<Word> due=new List<Word>();foreach(Word w in pool){MemoryState m=LoadMemory(w);if(m!=null&&m.Due<=DateTime.UtcNow)due.Add(w);}if(due.Count>0)return due[rnd.Next(due.Count)];int count=ReadString("NewDate","")==DateTime.Today.ToString("yyyy-MM-dd")?Read("NewCount",0,0,999):0;if(count<dailyNew){foreach(Word w in Shuffle(pool))if(ReadString("S_"+Hash(w.Key),"").Length==0)return w;}Word weakest=null;double lowest=2;foreach(Word w in pool){MemoryState m=LoadMemory(w);if(m==null)continue;double r=Recall(m,DateTime.UtcNow);if(r<lowest){lowest=r;weakest=w;}}return weakest!=null?weakest:(pool.Count>0?pool[rnd.Next(pool.Count)]:null);}
    void MarkSeen(Word w){string k="S_"+Hash(w.Key);if(ReadString(k,"").Length==0){string today=DateTime.Today.ToString("yyyy-MM-dd");int n=ReadString("NewDate","")==today?Read("NewCount",0,0,999):0;WriteString("NewDate",today);Write("NewCount",n+1);WriteString(k,"0|"+DateTime.UtcNow.Ticks+"|0|0");}}
    void Grade(Word w,int grade,bool revealed){DateTime now=DateTime.UtcNow;MemoryState m=LoadMemory(w);if(m==null)m=new MemoryState();if(grade==0){m.Bad++;m.Difficulty=Math.Min(10,m.Difficulty+0.8);m.Stability=Math.Max(0.05,m.Stability*0.35);m.Due=now.AddMinutes(10);}else{double r=Recall(m,now),factor=(11-m.Difficulty)/6.0,gain=(revealed?1.25:2.0)*factor*(0.35+(1-r)*1.6);if(m.Ok+m.Bad==0)m.Stability=revealed?0.5:2.0;else m.Stability=Math.Min(365,Math.Max(revealed?0.5:1.0,m.Stability*(1+gain)));m.Difficulty=Math.Max(1,Math.Min(10,m.Difficulty+(revealed?0.05:-0.2)));m.Ok++;m.Due=now.AddDays(m.Stability);}m.Last=now;SaveMemory(w,m);UpdateStreak();}
    MemoryState LoadMemory(Word w){string h=Hash(w.Key),raw=ReadString("M_"+h,"");string[] p=raw.Split('|');double s,d;long due,last;int ok,bad;if(p.Length==7&&p[0]=="A1"&&double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out s)&&double.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out d)&&long.TryParse(p[3],out due)&&int.TryParse(p[4],out ok)&&int.TryParse(p[5],out bad)&&long.TryParse(p[6],out last)){MemoryState m=new MemoryState();m.Stability=Math.Max(0.05,s);m.Difficulty=Math.Max(1,Math.Min(10,d));m.Due=new DateTime(due,DateTimeKind.Utc);m.Ok=ok;m.Bad=bad;m.Last=new DateTime(last,DateTimeKind.Utc);return m;}string old=ReadString("S_"+h,"");if(old.Length==0)return null;string[] q=old.Split('|');int stage=I(q,0);long ticks;if(!long.TryParse(q.Length>1?q[1]:"0",out ticks)||ticks<DateTime.MinValue.Ticks||ticks>DateTime.MaxValue.Ticks)ticks=DateTime.UtcNow.Ticks;double[] days={0.5,3,7,14,30,90};MemoryState legacy=new MemoryState();legacy.Stability=days[Math.Max(0,Math.Min(days.Length-1,stage))];legacy.Difficulty=5;legacy.Due=new DateTime(ticks,DateTimeKind.Utc);legacy.Ok=I(q,2);legacy.Bad=I(q,3);legacy.Last=legacy.Due.AddDays(-legacy.Stability);return legacy;}
    void SaveMemory(Word w,MemoryState m){string h=Hash(w.Key);WriteString("M_"+h,"A1|"+m.Stability.ToString("R",CultureInfo.InvariantCulture)+"|"+m.Difficulty.ToString("R",CultureInfo.InvariantCulture)+"|"+m.Due.Ticks+"|"+m.Ok+"|"+m.Bad+"|"+m.Last.Ticks);WriteString("S_"+h,"0|"+m.Due.Ticks+"|"+m.Ok+"|"+m.Bad);}
    static double Recall(MemoryState m,DateTime now){double elapsed=Math.Max(0,(now-m.Last).TotalDays);return Math.Pow(1+elapsed/(9*Math.Max(0.05,m.Stability)),-1);}
    void OpenWater(){if(wordCard!=null&&!wordCard.IsDisposed){pendingWater=true;SetFoot("喝水时间",TimeSpan.FromMinutes(30));return;}if(waterCard!=null&&!waterCard.IsDisposed){waterCard.Flash();return;}waterCard=new WaterForm(delegate{int n=ReadString("WaterDate","")==DateTime.Today.ToString("yyyy-MM-dd")?Read("WaterCount",0,0,99):0;WriteString("WaterDate",DateTime.Today.ToString("yyyy-MM-dd"));Write("WaterCount",n+1);nextWater=DateTime.Now.AddMinutes(waterMinutes);Play("happy");},delegate{nextWater=DateTime.Now.AddMinutes(snoozeMinutes);Play("wave");});waterCard.FormClosed+=delegate{waterCard=null;if(nextWater<=DateTime.Now)nextWater=DateTime.Now.AddMinutes(snoozeMinutes);SetFoot("",TimeSpan.Zero);if(pendingWord){pendingWord=false;BeginInvoke(new MethodInvoker(OpenWord));}};waterCard.Show(this);PositionBubble(waterCard);SetFoot("喝水时间",TimeSpan.FromMinutes(30));Play("wave");}
    void SetFoot(string text,TimeSpan keep){footStatus=text;statusUntil=DateTime.Now.Add(keep);Invalidate();}
    void OpenLockNotice(){if(lockCard!=null&&!lockCard.IsDisposed)return;lockCard=new NoticeForm("即将锁屏","还剩 1 分钟。移动鼠标、按键或点击按钮即可重新计时。",delegate{realInput=DateTime.UtcNow;warned=false;});lockCard.FormClosed+=delegate{lockCard=null;};lockCard.Show(this);PositionBubble(lockCard);}
    void PositionOpenBubbles(){if(wordCard!=null&&!wordCard.IsDisposed)PositionBubble(wordCard);if(waterCard!=null&&!waterCard.IsDisposed)PositionBubble(waterCard);if(lockCard!=null&&!lockCard.IsDisposed)PositionBubble(lockCard);}
    void PositionBubble(Form bubble){if(bubble==null||bubble.IsDisposed)return;Rectangle w=Screen.FromControl(this).WorkingArea;int x=Left+(Width-bubble.Width)/2,y=Top+petSide+4;if(y+bubble.Height>w.Bottom)y=Top-bubble.Height+4;if(x<w.Left)x=w.Left+6;if(x+bubble.Width>w.Right)x=w.Right-bubble.Width-6;if(y<w.Top)y=w.Top+6;bubble.Location=new Point(x,y);}
    bool IsFullScreenApp(){try{IntPtr h=Native.GetForegroundWindow();if(h==IntPtr.Zero)return false;Native.RECT r;if(!Native.GetWindowRect(h,out r))return false;Rectangle b=Screen.FromHandle(h).Bounds;return r.Left<=b.Left+2&&r.Top<=b.Top+2&&r.Right>=b.Right-2&&r.Bottom>=b.Bottom-2;}catch{return false;}}
    void OpenSettings(){using(SettingsForm f=new SettingsForm(idleMin,pulseMin,dailyNew,autoWordMinutes,waterMinutes,snoozeMinutes,petScale,awake,autoSpeak,autoWordEnabled,pauseFullscreen,waterEnabled,top,eyeFollow,AutoStart(),ReadString("BookMode","全部词库")))if(f.ShowDialog()==DialogResult.OK){idleMin=(int)f.Idle.Value;pulseMin=(int)f.Pulse.Value;dailyNew=(int)f.DailyNew.Value;autoWordMinutes=(int)f.AutoWordMinutes.Value;waterMinutes=(int)f.WaterMinutes.Value;snoozeMinutes=(int)f.Snooze.Value;petScale=(int)f.PetScale.Value;awake=f.Awake.Checked;autoSpeak=f.AutoSpeak.Checked;autoWordEnabled=f.AutoWordEnabled.Checked;pauseFullscreen=f.PauseFullscreen.Checked;waterEnabled=f.WaterEnabled.Checked;top=f.Top.Checked;eyeFollow=f.EyeFollow.Checked;TopMost=top;Write("Idle",idleMin);Write("Pulse",pulseMin);Write("DailyNew",dailyNew);Write("AutoWordMinutes",autoWordMinutes);Write("WaterMinutes",waterMinutes);Write("Snooze",snoozeMinutes);Write("PetScale",petScale);Write("Awake",awake?1:0);Write("AutoSpeak",autoSpeak?1:0);Write("AutoWordEnabled",autoWordEnabled?1:0);Write("PauseFullscreen",pauseFullscreen?1:0);Write("WaterEnabled",waterEnabled?1:0);Write("Top",top?1:0);Write("EyeFollow",eyeFollow?1:0);WriteString("BookMode",f.BookMode.SelectedItem.ToString());SetAuto(f.AutoStartBox.Checked);ApplySize();Location=Visible(Location);nextWater=DateTime.Now.AddMinutes(waterMinutes);nextAutoWord=DateTime.Now.AddMinutes(autoWordMinutes);realInput=lastPulse=DateTime.UtcNow;BuildTray();Invalidate();}}
    List<Word> Pool(){string b=ReadString("BookMode","全部词库");if(b=="IELTS")return new List<Word>(ielts);if(b=="日常高频")return new List<Word>(daily);List<Word> a=new List<Word>(ielts);a.AddRange(daily);return a;}
    IEnumerable<Word> Shuffle(List<Word> src){List<Word>a=new List<Word>(src);for(int i=a.Count-1;i>0;i--){int j=rnd.Next(i+1);Word x=a[i];a[i]=a[j];a[j]=x;}return a;}
    void LoadWords(){Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexPet.words.tsv");if(s==null)return;using(s)using(StreamReader r=new StreamReader(s,Encoding.UTF8)){string line;while((line=r.ReadLine())!=null){string[] p=line.Split('\t');if(p.Length<5)continue;Word w=new Word();w.Book=p[0];w.Text=p[1];w.Phonetic=p[2];w.Meaning=p[3];w.Example=p[4];if(w.Book=="IELTS")ielts.Add(w);else daily.Add(w);}}}
    new Image Load(string name){Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name);if(s==null)return null;using(s)using(Image raw=Image.FromStream(s)){Bitmap b=new Bitmap(raw.Width,raw.Height,PixelFormat.Format32bppArgb);using(Graphics g=Graphics.FromImage(b)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImage(raw,0,0);}return b;}}
    uint CurrentInputTick(){Native.LASTINPUTINFO x=new Native.LASTINPUTINFO();x.cbSize=(uint)Marshal.SizeOf(typeof(Native.LASTINPUTINFO));return Native.GetLastInputInfo(ref x)?x.dwTime:0;}
    void PollRealInput(){uint now=CurrentInputTick();if(now==0||now==lastInputTick)return;lastInputTick=now;if((DateTime.UtcNow-lastPulse).TotalSeconds<2)return;realInput=DateTime.UtcNow;warned=false;if(lockCard!=null&&!lockCard.IsDisposed)lockCard.Close();}
    void Session(object s,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock)locked=true;else if(e.Reason==SessionSwitchReason.SessionUnlock){locked=false;warned=false;realInput=lastPulse=DateTime.UtcNow;nextWater=DateTime.Now.AddMinutes(waterMinutes);nextAutoWord=DateTime.Now.AddMinutes(autoWordMinutes);}}
    void UpdateStreak(){string today=DateTime.Today.ToString("yyyy-MM-dd"),last=ReadString("StudyDate","");if(last==today)return;int n=last==DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd")?Read("Streak",0,0,9999)+1:1;Write("Streak",n);WriteString("StudyDate",today);}
    int Read(string n,int d,int lo,int hi){int v;using(RegistryKey k=Registry.CurrentUser.CreateSubKey(Reg)){object o=k.GetValue(n);v=o==null?d:Convert.ToInt32(o);}return Math.Max(lo,Math.Min(hi,v));}bool B(string n,bool d){return Read(n,d?1:0,0,1)==1;}void Write(string n,int v){using(RegistryKey k=Registry.CurrentUser.CreateSubKey(Reg))k.SetValue(n,v,RegistryValueKind.DWord);}string ReadString(string n,string d){using(RegistryKey k=Registry.CurrentUser.CreateSubKey(Reg)){object o=k.GetValue(n);return o==null?d:o.ToString();}}void WriteString(string n,string v){using(RegistryKey k=Registry.CurrentUser.CreateSubKey(Reg))k.SetValue(n,v,RegistryValueKind.String);}
    static int I(string[] p,int i){int n;return p.Length>i&&int.TryParse(p[i],out n)?n:0;}static string Hash(string s){unchecked{uint h=2166136261;foreach(char c in s){h^=c;h*=16777619;}return h.ToString("X8");}}
    bool AutoStart(){using(RegistryKey k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))return k!=null&&k.GetValue("CodexPetWin7")!=null;}void SetAuto(bool on){using(RegistryKey k=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(on)k.SetValue("CodexPetWin7","\""+Application.ExecutablePath+"\"");else k.DeleteValue("CodexPetWin7",false);}}
    new Point Visible(Point p){Rectangle w=Screen.FromPoint(p).WorkingArea;return new Point(Math.Max(w.Left,Math.Min(w.Right-Width,p.X)),Math.Max(w.Top,Math.Min(w.Bottom-Height,p.Y)));}
    protected override void Dispose(bool disposing){if(disposing){timer.Stop();tray.Visible=false;Microsoft.Win32.SystemEvents.SessionSwitch-=Session;if(atlas!=null)atlas.Dispose();}base.Dispose(disposing);}
}

static class Program {[STAThread]static void Main(){bool first;using(Mutex single=new Mutex(true,"Local\\CodexPetWin7.SingleInstance",out first)){if(!first){MessageBox.Show("Codex 桌宠已经在运行，请查看系统托盘。","Codex 桌宠",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);Application.ThreadException+=delegate(object s,System.Threading.ThreadExceptionEventArgs e){CrashLog.Write(e.Exception);MessageBox.Show("程序遇到异常，错误日志已保存到：\r\n"+CrashLog.PathName,"Codex 桌宠",MessageBoxButtons.OK,MessageBoxIcon.Error);};AppDomain.CurrentDomain.UnhandledException+=delegate(object s,UnhandledExceptionEventArgs e){Exception ex=e.ExceptionObject as Exception;if(ex!=null)CrashLog.Write(ex);};Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new PetForm());}}}
}
