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

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void Execute(ICommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear(); // 执行新命令后清除重做栈

            // 限制历史记录大小
            if (_undoStack.Count > MaxHistorySize)
            {
                var commands = _undoStack.ToList();
                commands.RemoveAt(0);
                _undoStack.Clear();
                foreach (var cmd in commands)
                    _undoStack.Push(cmd);
            }

            OnCommandExecuted?.Invoke(command);
        }

        public void Undo()
        {
            if (!CanUndo) return;

            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);

            OnCommandUndone?.Invoke(command);
        }

        public void Redo()
        {
            if (!CanRedo) return;

            var command = _redoStack.Pop();
            command.Execute();
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

