using System;
using PiggyBuddy;
class CoreTests {
 static int count;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);count++;}
 static void Main(){
  var t=new TimerCore(60,30);
  Check(t.Advance(59,true)==Transition.None && t.Remaining==1,"countdown");
  Check(t.Advance(1,true)==Transition.RestStarted && t.Remaining==30,"work boundary");
  Check(t.Advance(30,true)==Transition.WorkStarted && t.Completed==1,"automatic next round");
  t.Paused=true;t.Advance(30,true);Check(t.Remaining==60,"pause preserves time");
  t.Paused=false;t.Advance(30,false);Check(t.Remaining==60,"quiet period preserves time");
  t.Advance(40,true);t.Advance(500,true);Check(!t.IsRest && t.Remaining==60,"sleep resume reset");
  t.Snooze();Check(!t.IsRest && t.Remaining==300,"snooze");
  Check(TimerCore.IsAllowed(true,540,1320,540) && !TimerCore.IsAllowed(true,540,1320,1320),"daily boundaries");
  Check(TimerCore.IsAllowed(true,1320,360,30) && !TimerCore.IsAllowed(true,1320,360,720),"overnight hours");
  Check(TimerCore.IsAllowed(true,540,540,20),"equal times all day");
  t.Advance(double.NaN,true);Check(t.Remaining==300,"invalid elapsed");
  Console.WriteLine("PASS: "+count+" timer checks");
 }
}
