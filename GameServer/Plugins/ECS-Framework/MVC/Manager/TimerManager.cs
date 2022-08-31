//using System;
//using System.Collections.Generic;


//namespace MVC
//{
//    public class TimerManager : Singleton<TimerManager>
//    {

//        public enum TimerType
//        {
//            FIXED_DURATION,
//            FIXED_REALTIME_DURATION,
//            EVERY_FRAME,
//        }

//        public class TimerTask
//        {
//            public TimerType timerType;
//            public float startTime;
//            public float interval;
//            public int repeatTimes;
//            public Func<bool> executable;

//            private int executeTimes;
//            private float deltaTime;
//            private bool started;
//            private bool stopped;

//            public bool IsStopped { get => stopped; }

//            public void Stop()
//            {
//                stopped = true;
//            }

//            internal bool Update(float delta)
//            {
//                if (timerType == TimerType.FIXED_DURATION)
//                    deltaTime += delta;

//                if (!started)
//                {
//                    if (timerType == TimerType.FIXED_REALTIME_DURATION)
//                    {
//                        if (Time.realtimeSinceStartup >= startTime)
//                        {
//                            started = true;
//                            deltaTime = startTime + interval;
//                            return true;
//                        }
//                    }
//                    else
//                    {
//                        if (deltaTime >= startTime)
//                        {
//                            started = true;
//                            deltaTime -= startTime;
//                            return true;
//                        }
//                    }
//                }
//                else
//                {
//                    if (timerType == TimerType.EVERY_FRAME)
//                    {
//                        deltaTime = 0;
//                        return true;
//                    }

//                    if (timerType == TimerType.FIXED_REALTIME_DURATION)
//                    {
//                        if (Time.realtimeSinceStartup >= deltaTime)
//                        {
//                            deltaTime += interval;
//                            return true;
//                        }
//                    }
//                    else
//                    {
//                        if (deltaTime >= interval)
//                        {
//                            deltaTime -= interval;
//                            return true;
//                        }
//                    }
//                }

//                return false;
//            }

//            /// <summary>
//            /// Execute this instance.
//            /// </summary>
//            /// <returns>if <see langword="true"/>, remove this task</returns>
//            internal bool Execute()
//            {
//                executeTimes++;
//                if (executable != null)
//                {
//                    try
//                    {
//                        return stopped || executable() || executeTimes == repeatTimes;
//                    }
//                    catch (Exception e)
//                    {
//                        Debug.LogError(e);
//                        return true;
//                    }
//                }
//                else
//                    return true;
//            }
//        }

//#if ODIN_INSPECTOR
//        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
//#endif
//        private readonly List<TimerTask> m_TaskList = new List<TimerTask>();
//        private readonly List<TimerTask> m_ToBeRemoved = new List<TimerTask>();

//        public float DeltaTime { get; private set; }

//        protected override void OnDelete()
//        {
//            m_TaskList.Clear();
//            base.OnDelete();
//        }

//        protected override void OnUpdate(float delta)
//        {
//            DeltaTime = delta;
//            for (int i = 0; i < m_TaskList.Count; ++i)
//            {
//                TimerTask task = m_TaskList[i];
//                if (task.Update(delta) && task.Execute())
//                {
//                    task.Stop();
//                    m_ToBeRemoved.Add(task);
//                }
//            }

//            if (m_ToBeRemoved.Count > 0)
//            {
//                for (int i = 0; i < m_ToBeRemoved.Count; ++i)
//                {
//                    m_TaskList.Remove(m_ToBeRemoved[i]);
//                }
//                m_ToBeRemoved.Clear();
//            }
//        }

//        /// <summary>
//        /// Adds the one shot task.
//        /// </summary>
//        /// <param name="startDelay">Start delay.</param>
//        /// <param name="executable">Executable.</param>
//        public TimerTask AddOneShotTask(float startDelay, Action executable)
//        {
//            return AddTask(TimerType.FIXED_DURATION, startDelay, 0, 1, () =>
//            {
//                executable?.Invoke();
//                return true;
//            });
//        }

//        /// <summary>
//        /// Adds the realtime one shot task.
//        /// </summary>
//        /// <param name="startDelay">Start delay.</param>
//        /// <param name="executable">Executable.</param>
//        public TimerTask AddRealtimeOneShotTask(float startDelay, Action executable)
//        {
//            return AddTask(TimerType.FIXED_REALTIME_DURATION, Time.realtimeSinceStartup + startDelay, 0, 1, () =>
//            {
//                executable?.Invoke();
//                return true;
//            });
//        }

//        /// <summary>
//        /// Adds the repeat task.
//        /// </summary>
//        /// <param name="startDelay">Start delay.</param>
//        /// <param name="interval">Interval.</param>
//        /// <param name="repeatTimes">Repeat times, -1 means always.</param>
//        /// <param name="executable">Executable.</param>
//        public TimerTask AddRepeatTask(float startDelay, float interval, int repeatTimes, Func<bool> executable)
//        {
//            return AddTask(TimerType.FIXED_DURATION, startDelay, interval, repeatTimes, executable);
//        }

//        /// <summary>
//        /// Adds the realtime repeat task.
//        /// </summary>
//        /// <param name="startDelay">Start delay.</param>
//        /// <param name="interval">Interval.</param>
//        /// <param name="repeatTimes">Repeat times, -1 means always.</param>
//        /// <param name="executable">Executable.</param>
//        public TimerTask AddRealtimeRepeatTask(float startDelay, float interval, int repeatTimes, Func<bool> executable)
//        {
//            return AddTask(TimerType.FIXED_REALTIME_DURATION, Time.realtimeSinceStartup + startDelay, interval, repeatTimes, executable);
//        }

//        /// <summary>
//        /// Adds the frame execute task.
//        /// </summary>
//        /// <param name="executable">Executable.</param>
//        public TimerTask AddFrameExecuteTask(Func<bool> executable)
//        {
//            return AddTask(TimerType.EVERY_FRAME, 0, 0, 0, executable);
//        }

//        /// <summary>
//        /// Adds the task.
//        /// </summary>
//        /// <returns>The task.</returns>
//        /// <param name="timerType">Timer type.</param>
//        /// <param name="startTime">Start time.</param>
//        /// <param name="interval">Interval.</param>
//        /// <param name="repeatTimes">Repeat times.</param>
//        /// <param name="executable">Executable.</param>
//        private TimerTask AddTask(TimerType timerType, float startTime, float interval, int repeatTimes, Func<bool> executable)
//        {
//            TimerTask task = new TimerTask { timerType = timerType, startTime = startTime, interval = interval, repeatTimes = repeatTimes, executable = executable };

//            m_TaskList.Add(task);

//            return task;
//        }
//    }
//}
