﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreneticGameCore
{
    /// <summary>
    /// Helper to schedule sync or async tasks.
    /// </summary>
    public class Scheduler
    {
        /// <summary>
        /// Current set of tasks.
        /// TODO: Could be a ConcurrentQueue? Probably should be, for that matter!
        /// </summary>
        public LockedLinkedList<SyncScheduleItem> Tasks = new LockedLinkedList<SyncScheduleItem>();

        /// <summary>
        /// Gets a sync task object, not yet scheduled.
        /// </summary>
        /// <param name="act">The action to call.</param>
        /// <param name="delay">The delay value.</param>
        /// <returns>A schedule item.</returns>
        public SyncScheduleItem GetSyncTask(Action act, double delay = 0)
        {
            return new SyncScheduleItem() { MyAction = act, Time = delay, OwningEngine = this };
        }

        /// <summary>
        /// Creates and schedules a sync task.
        /// </summary>
        /// <param name="act">The action to run.</param>
        /// <param name="delay">How long before the task is called.</param>
        /// <returns>The scheduled item.</returns>
        public SyncScheduleItem ScheduleSyncTask(Action act, double delay = 0)
        {
            SyncScheduleItem item = new SyncScheduleItem() { MyAction = act, Time = delay, OwningEngine = this };
            Tasks.AddAtEnd(item);
            return item;
        }

        /// <summary>
        /// Ran every frame to cause all sync tasks to be processed.
        /// </summary>
        /// <param name="time">The delta time.</param>
        public void RunAllSyncTasks(double time)
        {
            LockedLinkedList<SyncScheduleItem>.Node node = Tasks.First;
            while (node != null)
            {
                node.Data.Time -= time;
                if (node.Data.Time > 0)
                {
                    node = node.Next;
                    continue;
                }
                try
                {
                    node.Data.MyAction.Invoke();
                }
                catch (Exception ex)
                {
                    if (ex is ThreadAbortException)
                    {
                        throw ex;
                    }
                    SysConsole.Output("Handling sync task", ex);
                }
                LockedLinkedList<SyncScheduleItem>.Node torem = node;
                node = node.Next;
                Tasks.Remove(torem);
            }
        }

        /// <summary>
        /// Starts an async task.
        /// </summary>
        /// <param name="a">The action to launch async.</param>
        /// <returns>The scheduled item.</returns>
        public ASyncScheduleItem StartAsyncTask(Action a)
        {
            ASyncScheduleItem asyncer = new ASyncScheduleItem() { OwningEngine = this, MyAction = a };
            asyncer.RunMe();
            return asyncer;
        }

        /// <summary>
        /// Creates but does not start an async task.
        /// </summary>
        /// <param name="a">The action to launch async.</param>
        /// <param name="followUp">Optional followup task.</param>
        /// <returns>The created schedule item.</returns>
        public ASyncScheduleItem AddAsyncTask(Action a, ASyncScheduleItem followUp = null)
        {
            ASyncScheduleItem asyncer = new ASyncScheduleItem() { OwningEngine = this, MyAction = a, FollowUp = followUp };
            return asyncer;
        }
    }

    /// <summary>
    /// Represents a schedulable item.
    /// </summary>
    public abstract class ScheduleItem
    {
        /// <summary>
        /// Runs the schedulable item.
        /// </summary>
        public abstract void RunMe();

        /// <summary>
        /// The relevant scheduler.
        /// </summary>
        public Scheduler OwningEngine;
    }

    /// <summary>
    /// Represents a synchronous scheduled item.
    /// </summary>
    public class SyncScheduleItem : ScheduleItem
    {
        /// <summary>
        /// The action to run.
        /// </summary>
        public Action MyAction;

        /// <summary>
        /// The time left before running.
        /// </summary>
        public double Time = 0;

        /// <summary>
        /// Causes the action to be run at the next frame.
        /// </summary>
        public override void RunMe()
        {
            OwningEngine.ScheduleSyncTask(MyAction);
        }
    }

    /// <summary>
    /// Represents an asynchronous running item.
    /// </summary>
    public class ASyncScheduleItem : ScheduleItem
    {
        /// <summary>
        /// The action to run.
        /// </summary>
        public Action MyAction;

        /// <summary>
        /// The next thing to run in this sequence.
        /// </summary>
        public ASyncScheduleItem FollowUp = null;

        /// <summary>
        /// Locker to prevent thread issues.
        /// </summary>
        Object Locker = new Object();

        /// <summary>
        /// Whether the item has been started.
        /// </summary>
        public bool Started = false;

        /// <summary>
        /// Whether the item is complete.
        /// </summary>
        bool Done = false;

        /// <summary>
        /// Gets whether the item has started.
        /// </summary>
        public bool HasStarted()
        {
            lock (Locker)
            {
                return Started;
            }
        }

        /// <summary>
        /// Gets whether the item is complete.
        /// </summary>
        public bool IsDone()
        {
            lock (Locker)
            {
                return Done;
            }
        }

        /// <summary>
        /// Replaces the schedule item if its not yet started, otherwises follows it with a new item.
        /// </summary>
        /// <param name="item">The replacement item.</param>
        /// <returns>The final item.</returns>
        public ASyncScheduleItem ReplaceOrFollowWith(ASyncScheduleItem item)
        {
            lock (Locker)
            {
                if (FollowUp != null)
                {
                    return FollowUp.ReplaceOrFollowWith(item);
                }
                if (Started)
                {
                    if (Done)
                    {
                        item.RunMe();
                        return item;
                    }
                    else
                    {
                        FollowUp = item;
                        return item;
                    }
                }
                else
                {
                    MyAction = item.MyAction;
                    FollowUp = item.FollowUp;
                    return this;
                }
            }
        }

        /// <summary>
        /// Tells the item to follow the current item with a new one.
        /// </summary>
        /// <param name="item">The follower item.</param>
        public void FollowWith(ASyncScheduleItem item)
        {
            lock (Locker)
            {
                if (Done)
                {
                    item.RunMe();
                }
                else
                {
                    FollowUp = item;
                }
            }
        }

        /// <summary>
        /// Runs the item asynchronously immediately.
        /// </summary>
        public override void RunMe()
        {
            lock (Locker)
            {
                if (Started && !Done)
                {
                    return;
                }
                Started = true;
                Done = false;
            }
            Task.Factory.StartNew(RunInternal);
        }

        /// <summary>
        /// Internal runner for the item.
        /// </summary>
        private void RunInternal()
        {
            MyAction.Invoke();
            lock (Locker)
            {
                Done = true;
            }
            if (FollowUp != null)
            {
                FollowUp.RunMe();
            }
        }
    }
}