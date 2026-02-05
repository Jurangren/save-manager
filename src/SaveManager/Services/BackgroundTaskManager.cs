using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace SaveManager.Services
{
    /// <summary>
    /// 后台任务管理器 - 跟踪和管理所有后台云同步任务
    /// </summary>
    public class BackgroundTaskManager
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly ConcurrentDictionary<string, Task> activeTasks = new ConcurrentDictionary<string, Task>();
        private int taskCounter = 0;

        /// <summary>
        /// 当前活跃任务数量
        /// </summary>
        public int ActiveTaskCount => activeTasks.Count;

        /// <summary>
        /// 是否有活跃任务
        /// </summary>
        public bool HasActiveTasks => activeTasks.Count > 0;

        /// <summary>
        /// 注册并执行一个后台任务
        /// </summary>
        public void RunTask(string taskName, Func<Task> taskFunc)
        {
            var taskId = $"{taskName}_{Interlocked.Increment(ref taskCounter)}";
            
            var task = Task.Run(async () =>
            {
                try
                {
                    logger.Info($"Background task started: {taskId}");
                    await taskFunc();
                    logger.Info($"Background task completed: {taskId}");
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Background task failed: {taskId}");
                }
                finally
                {
                    // 任务完成后从字典中移除
                    activeTasks.TryRemove(taskId, out _);
                }
            });

            activeTasks.TryAdd(taskId, task);
        }

        /// <summary>
        /// 等待所有活跃任务完成
        /// </summary>
        /// <param name="timeout">超时时间</param>
        /// <returns>是否所有任务都已完成</returns>
        public async Task<bool> WaitForAllTasksAsync(TimeSpan timeout)
        {
            if (!HasActiveTasks)
            {
                return true;
            }

            var tasks = activeTasks.Values.ToArray();
            if (tasks.Length == 0)
            {
                return true;
            }

            logger.Info($"Waiting for {tasks.Length} background tasks to complete...");

            try
            {
                var completedTask = await Task.WhenAny(
                    Task.WhenAll(tasks),
                    Task.Delay(timeout)
                );

                // 检查是否超时
                if (completedTask != Task.WhenAll(tasks))
                {
                    logger.Warn($"Timeout waiting for background tasks. {activeTasks.Count} tasks still running.");
                    return false;
                }

                logger.Info("All background tasks completed.");
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error waiting for background tasks");
                return false;
            }
        }

        /// <summary>
        /// 同步等待所有活跃任务完成
        /// </summary>
        /// <param name="timeout">超时时间</param>
        /// <returns>是否所有任务都已完成</returns>
        public bool WaitForAllTasks(TimeSpan timeout)
        {
            return WaitForAllTasksAsync(timeout).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 获取所有活跃任务的名称
        /// </summary>
        public string[] GetActiveTaskNames()
        {
            return activeTasks.Keys.ToArray();
        }
    }
}
