using System;
namespace PiggyBuddy {
 public enum Transition { None, RestStarted, WorkStarted }
 public sealed class TimerCore {
  public double WorkSeconds, RestSeconds, Remaining;
  public bool IsRest, Paused;
  public int Completed;
  public TimerCore(double work, double rest) { WorkSeconds=work; RestSeconds=rest; Reset(); }
  public void Reset() { IsRest=false; Remaining=WorkSeconds; }
  public void Snooze() { IsRest=false; Remaining=300; Paused=false; }
  public Transition Advance(double elapsed, bool allowed) {
   if(Paused || !allowed || elapsed<=0 || double.IsNaN(elapsed) || double.IsInfinity(elapsed))return Transition.None;
   // Resume after a long suspend: fresh work round, no stale reminder burst.
   if(elapsed>90) { Reset(); return Transition.None; }
   Remaining-=elapsed;
   if(Remaining>0)return Transition.None;
   IsRest=!IsRest;
   Remaining=IsRest?RestSeconds:WorkSeconds;
   if(!IsRest)Completed++;
   return IsRest?Transition.RestStarted:Transition.WorkStarted;
  }
  public static bool IsAllowed(bool enabled,int start,int end,int minute) {
   if(!enabled || start==end)return true;
   return start<end ? minute>=start && minute<end : minute>=start || minute<end;
  }
 }
}
