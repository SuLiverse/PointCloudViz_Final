namespace PointCloudViz.Core.History;

/// <summary>可撤销命令（命令模式）。</summary>
public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

/// <summary>以委托实现的命令，适合"切换前后两种状态"的场景。</summary>
public sealed class DelegateCommand(string description, Action execute, Action undo) : IUndoableCommand
{
    public string Description { get; } = description;
    public void Execute() => execute();
    public void Undo() => undo();
}

/// <summary>
/// 撤销/重做栈，容量有限（超出时丢弃最早的记录）。
/// 旧版每次超限都把整个栈转成 List 再重建，这里用双端链表 O(1) 完成。
/// </summary>
public sealed class UndoRedoStack(int capacity = 30)
{
    private readonly LinkedList<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    public int Capacity { get; } = Math.Max(1, capacity);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public string? UndoDescription => _undo.Last?.Value.Description;
    public string? RedoDescription => _redo.TryPeek(out var c) ? c.Description : null;

    /// <summary>撤销栈中的操作描述（最早在前）。</summary>
    public IEnumerable<string> History => _undo.Select(c => c.Description);

    /// <summary>撤销/重做状态变化时触发。</summary>
    public event EventHandler? Changed;

    /// <summary>执行命令并入栈。</summary>
    public void Execute(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        Push(command);
    }

    /// <summary>将已经执行过的命令入栈。</summary>
    public void Push(IUndoableCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _undo.AddLast(command);
        if (_undo.Count > Capacity) _undo.RemoveFirst();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (_undo.Last is not { } node) return false;
        _undo.RemoveLast();
        node.Value.Undo();
        _redo.Push(node.Value);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var command)) return false;
        command.Execute();
        _undo.AddLast(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
