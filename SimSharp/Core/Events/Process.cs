﻿#region License Information
/* SimSharp - A .NET port of SimPy, discrete event simulation framework
Copyright (C) 2014  Heuristic and Evolutionary Algorithms Laboratory (HEAL)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.*/
#endregion
using System;
using System.Collections.Generic;

namespace SimSharp {
  public class Process : Event {
    private readonly IEnumerator<Event> generator;
    private Event target;
    public bool IsFaulted { get; protected set; }
    public object Fault { get; protected set; }

    public Process(Environment environment, IEnumerable<Event> generator)
      : base(environment) {
      this.generator = generator.GetEnumerator();
      target = new Initialize(environment, this);
    }

    public override Event Fail(object value = null, bool urgent = false) {
      IsFaulted = true;
      return base.Fail(value, urgent);
    }

    public virtual void Interrupt(object cause = null) {
      if (IsScheduled) throw new InvalidOperationException("The process has terminated and cannot be interrupted.");
      if (Environment.ActiveProcess == this) throw new InvalidOperationException("A process is not allowed to interrupt itself.");

      var interruptEvent = new Event(Environment);
      interruptEvent.CallbackList.Add(Resume);
      interruptEvent.Fail(cause, urgent: true);
    }

    protected virtual void Resume(Event @event) {
      if (IsScheduled) return;
      if (@event != target) target.CallbackList.Remove(Resume);
      Environment.ActiveProcess = this;
      if (@event.IsOk) {
        bool hasMoved;
        try {
          hasMoved = generator.MoveNext();
          if (IsScheduled) return; // the generator called e.g. Environment.ActiveProcess.Fail
        } catch (Exception exc) {
          Fail(exc, urgent: true);
          return;
        } finally {
          Environment.ActiveProcess = null;
        }
        if (hasMoved) ProceedToEvent();
        else FinishProcess(@event.Value);
      } else {
        // Fault handling differs from SimPy as in .NET it is not possible to inject an
        // exception into an enumerator. It is even impossible to put a yield return inside
        // a try-catch block, so here the Process will set IsFaulted and will then move to
        // the next yield in the generator. However, if after this move IsFaulted is still
        // true we know that the error was not handled. It is assumed the error is handled
        // if HandleFault() is called on the environment's ActiveProcess which will reset
        // the flag.
        IsFaulted = true;
        Fault = @event.Value;

        bool hasMoved;
        try {
          hasMoved = generator.MoveNext();
          if (IsScheduled) return; // the generator called e.g. Environment.ActiveProcess.Fail
        } catch (Exception exc) {
          Fail(exc, urgent: true);
          return;
        } finally {
          Environment.ActiveProcess = null;
        }

        if (hasMoved) {
          // if we move next, but IsFaulted is still true
          if (IsFaulted) throw new InvalidOperationException("The process continued despite being faulted.");
          // otherwise HandleFault was called and the fault was handled
          ProceedToEvent();
        } else if (IsFaulted) throw new InvalidOperationException("The process cannot finish when it is faulted.");
        else FinishProcess(@event.Value);
      }
      Environment.ActiveProcess = null;
    }

    protected virtual void ProceedToEvent() {
      target = generator.Current;
      if (target.CallbackList != null)
        target.CallbackList.Add(Resume);
      else throw new InvalidOperationException("Resuming on an event that was already triggered.");
    }

    protected virtual void FinishProcess(object value = null) {
      IsOk = true;
      Value = value;
      IsScheduled = true;
      Environment.Schedule(this);
    }

    public virtual bool HandleFault() {
      if (!IsFaulted) return false;
      IsFaulted = false;
      return true;
    }

    private class Initialize : Event {
      public Initialize(Environment environment, Process process)
        : base(environment) {
        CallbackList.Add(process.Resume);
        IsOk = true;
        IsScheduled = true;
        environment.Schedule(this);
      }
    }
  }
}
