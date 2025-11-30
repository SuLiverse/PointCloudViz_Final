using System;
using System.Collections.Generic;

namespace PointCloudViz_Final.Patterns
{
    /// <summary>观察者模式：可观察的进度对象</summary>
    public class ProgressObservable
    {
        private readonly List<IProgressObserver> _observers = new();

        public void AddObserver(IProgressObserver observer)
        {
            if (!_observers.Contains(observer))
                _observers.Add(observer);
        }

        public void RemoveObserver(IProgressObserver observer)
        {
            _observers.Remove(observer);
        }

        protected void NotifyProgress(double percent, string? message = null)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    observer.OnProgressChanged(percent, message);
                }
                catch
                {
                    // 忽略观察者错误，避免影响主流程
                }
            }
        }
    }
}

