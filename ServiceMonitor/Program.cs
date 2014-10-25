﻿using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace ServiceMonitor
{
    class Program
    {
        static readonly string _serviceName = ConfigurationManager.AppSettings["serviceName"];
        static readonly int _startTimeout = int.Parse(ConfigurationManager.AppSettings["startTimeout"]);
        static readonly ScheduleService _scheduleService = new ScheduleService();

        static void Main(string[] args)
        {
            _scheduleService.ScheduleTask("MonitorServiceAlive", MonitorServiceStatus, 1000, 1000);
            Console.WriteLine("Service monitor started, monitoring service '{0}'. Press any key to exit.", _serviceName);
            Console.ReadLine();
        }

        static void MonitorServiceStatus()
        {
            var serviceRunning = Process.GetProcessesByName(_serviceName).Count() > 0;
            if (!serviceRunning)
            {
                Console.WriteLine("'{0}' is stopped, try to restart it.", _serviceName);
                RestartService();
            }
        }
        static void RestartService()
        {
            try
            {
                var serviceController = new ServiceController(_serviceName);
                serviceController.Start();
                serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(_startTimeout));
                Console.WriteLine("'{0}' restart successfully.", _serviceName);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Restart service '{0}' failed, exception:{1}", _serviceName, ex);
            }
        }
    }

    class ScheduleService
    {
        private readonly ConcurrentDictionary<int, TimerBasedTask> _taskDict = new ConcurrentDictionary<int, TimerBasedTask>();
        private int _maxTaskId;

        public int ScheduleTask(string actionName, Action action, int dueTime, int period)
        {
            var newTaskId = Interlocked.Increment(ref _maxTaskId);
            var timer = new Timer((obj) =>
            {
                var currentTaskId = (int)obj;
                TimerBasedTask currentTask;
                if (_taskDict.TryGetValue(currentTaskId, out currentTask))
                {
                    if (currentTask.Stoped)
                    {
                        return;
                    }

                    try
                    {
                        currentTask.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                        if (currentTask.Stoped)
                        {
                            return;
                        }
                        currentTask.Action();
                    }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Task has exception, actionName:{0}, dueTime:{1}, period:{2}, exception:{3}", currentTask.ActionName, currentTask.DueTime, currentTask.Period, ex);
                    }
                    finally
                    {
                        if (!currentTask.Stoped)
                        {
                            try
                            {
                                currentTask.Timer.Change(currentTask.Period, currentTask.Period);
                            }
                            catch (ObjectDisposedException) { }
                        }
                    }
                }
            }, newTaskId, Timeout.Infinite, Timeout.Infinite);

            if (!_taskDict.TryAdd(newTaskId, new TimerBasedTask { ActionName = actionName, Action = action, Timer = timer, DueTime = dueTime, Period = period, Stoped = false }))
            {
                Console.WriteLine("Schedule task failed, actionName:{0}, dueTime:{1}, period:{2}", actionName, dueTime, period);
                return -1;
            }

            timer.Change(dueTime, period);

            return newTaskId;
        }
        public void ShutdownTask(int taskId)
        {
            TimerBasedTask task;
            if (_taskDict.TryRemove(taskId, out task))
            {
                task.Stoped = true;
                task.Timer.Change(Timeout.Infinite, Timeout.Infinite);
                task.Timer.Dispose();
            }
        }

        class TimerBasedTask
        {
            public string ActionName;
            public Action Action;
            public Timer Timer;
            public int DueTime;
            public int Period;
            public bool Stoped;
        }
    }
}