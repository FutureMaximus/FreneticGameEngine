﻿//
// This file is part of the Frenetic Game Engine, created by Frenetic LLC.
// This code is Copyright (C) Frenetic LLC under the terms of a strict license.
// See README.md or LICENSE.txt in the FreneticGameEngine source root for the contents of the license.
// If neither of these are available, assume that neither you nor anyone other than the copyright holder
// hold any right or permission to use this software until such time as the official license is identified.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FGECore.StackNoteSystem;
using FGECore.EntitySystem;
using FGECore.FileSystems;
using FreneticUtilities.FreneticToolkit;
using System.Threading;
using FGECore.UtilitySystems;

namespace FGECore.CoreSystems
{
    /// <summary>
    /// Represents one game instance - that is, one program, which contains an arbitrary number of engines within it.
    /// </summary>
    public abstract class GameInstance<T, T2> where T : BasicEntity<T, T2> where T2 : BasicEngine<T, T2>
    {
        /// <summary>
        /// Any and all engines running in this instance on the main level.
        /// </summary>
        public List<T2> Engines = new List<T2>();

        /// <summary>
        /// Gets the "default" engine: the first in the <see cref="Engines"/> list!
        /// </summary>
        public T2 DefaultEngine
        {
            get
            {
                return Engines[0];
            }
        }

        /// <summary>
        /// Whether the instance is marked for shutdown as soon as possible.
        /// </summary>
        public readonly CancellationTokenSource NeedShutdown = new CancellationTokenSource();

        /// <summary>
        /// The name of the data folder. By default, "data".
        /// </summary>
        public string Folder_Data = "data";

        /// <summary>
        /// The name of the mods folder. By default, "mods".
        /// </summary>
        public string Folder_Mods = "mods";

        /// <summary>
        /// The name of the saves folder. By default, "saves".
        /// </summary>
        public string Folder_Saves = "saves";

        /// <summary>
        /// Inits the game instance.
        /// </summary>
        public void InstanceInit()
        {
            SysConsole.Output(OutputType.INIT, "GameInstance loading file helpers...");
            Files.Init(Folder_Data, Folder_Mods, Folder_Saves);
            AssetStreaming = new AssetStreamingEngine(Files, Schedule);
            AssetStreaming.Init();
        }

        /// <summary>
        /// How much time has passed since the instance first loaded.
        /// </summary>
        public double GlobalTickTime = 1;

        /// <summary>
        /// The current delta timing for the instance tick.
        /// Represents the amount of time passed since the last tick.
        /// </summary>
        public double Delta = 0;

        /// <summary>
        /// Lock this object to prevent collision with the instance tick.
        /// Only lock on this if the scheduler does not fit your needs. (Consider requesting new scheduler features if that is the case!)
        /// </summary>
        public LockObject TickLock = new LockObject();

        /// <summary>
        /// The scheduling system for this game instance.
        /// </summary>
        public Scheduler Schedule = new Scheduler();

        /// <summary>
        /// Helper for files.
        /// </summary>
        public FileEngine Files = new FileEngine();

        /// <summary>
        /// The source object for this instance. Set to any tag style constant reference you find most helpful to keep!
        /// </summary>
        public Object Source;

        /// <summary>
        /// Helper for streaming assets.
        /// </summary>
        public AssetStreamingEngine AssetStreaming;

        /// <summary>
        /// Random helper object.
        /// </summary>
        public MTRandom RandomHelper = new MTRandom();

        /// <summary>
        /// Does some pre-tick processing. Call <see cref="Tick"/> after.
        /// </summary>
        /// <param name="delta">How much time has passed since the last tick.</param>
        public void PreTick(double delta)
        {
            Delta = delta;
            GlobalTickTime += delta;
            TickScheduler();
        }

        /// <summary>
        /// Ticks the instance's scheduler.
        /// </summary>
        public void TickScheduler()
        {
            try
            {
                StackNoteHelper.Push("GameInstance - Tick Scheduler", Schedule);
                Schedule.RunAllSyncTasks(Delta);
            }
            finally
            {
                StackNoteHelper.Pop();
            }
        }

        /// <summary>
        /// Additional action to call during the instance tick, if any.
        /// </summary>
        public Action OnTick;

        /// <summary>
        /// Ticks the instance and all engines.
        /// Called automatically by the standard run thread.
        /// Call <see cref="PreTick(double)"/> first.
        /// </summary>
        public void Tick()
        {
            try
            {
                StackNoteHelper.Push("GameInstance tick sequence - Tick", this);
                foreach (T2 engine in Engines)
                {
                    engine.Delta = Delta;
                    engine.Tick();
                }
                OnTick?.Invoke();
            }
            finally
            {
                StackNoteHelper.Pop();
            }
        }
    }
}
