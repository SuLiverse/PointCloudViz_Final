using System;
using System.Collections.Generic;
using System.Linq;

namespace PointCloudViz_Final.Patterns
{
    /// <summary>命令模式：命令管理器（支持撤销/重做）</summary>
    public class CommandManager
    {
        private readonly Stack<ICommand> _undoStack = new();
        private readonly Stack<ICommand> _redoStack = new();
        private const int MaxHistorySize = 50;
        private const long MaxHistoryBytes = 256L * 1024 * 1024;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Execute(ICommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear(); // 执行新命令后清除重做栈

            // Keep at least the latest change undoable; evict oldest snapshots first.
            if (_undoStack.Count > MaxHistorySize || _undoStack.Sum(c => c.RetainedBytes) > MaxHistoryBytes)
            {
                var commands = _undoStack.ToList();
                long retainedBytes = commands.Sum(c => c.RetainedBytes);
                while (commands.Count > 1 && (commands.Count > MaxHistorySize || retainedBytes > MaxHistoryBytes))
                {
                    retainedBytes -= commands[^1].RetainedBytes;
                    commands.RemoveAt(commands.Count - 1);
                }
                _undoStack.Clear();
                for (int i = commands.Count - 1; i >= 0; i--)
                    _undoStack.Push(commands[i]);
            }

            OnCommandExecuted?.Invoke(command);
        }

        public void Undo()
        {
            if (!CanUndo) return;

            var command = _undoStack.Peek();
            command.Undo();
            _undoStack.Pop();
            _redoStack.Push(command);

            OnCommandUndone?.Invoke(command);
        }

        public void Redo()
        {
            if (!CanRedo) return;

            var command = _redoStack.Peek();
            command.Execute();
            _redoStack.Pop();
            _undoStack.Push(command);

            OnCommandRedone?.Invoke(command);
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        public event Action<ICommand>? OnCommandExecuted;
        public event Action<ICommand>? OnCommandUndone;
        public event Action<ICommand>? OnCommandRedone;
    }
}

