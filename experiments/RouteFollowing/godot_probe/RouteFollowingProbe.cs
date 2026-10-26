using Godot;
using Rts.Movement.Prototype;
using Rts.Kernel;
using Rts.Kernel.Numerics;
using System.Diagnostics;

/// <summary>Input and rendering only. All positions, waits and completion come from the C# experiment.</summary>
public partial class RouteFollowingProbe : Node2D
{
    private RouteWorld _world = null!;
    private readonly HashSet<ulong> _selected = [];
    private Font _font = null!;
    private const float MapScale = 0.625f;
    private static readonly Vector2 MapOffset = new(35, 120);
    private double _accumulator;
    private double _peak;
    private int _scenario = 5;
    private bool _routes = true;
    private bool _paused;
    private string _saved = "";
    private string _message = "";
    private Vector2? _drag;
    private long _replayStart = -1;
    private int _replayIndex;
    private int _renderedFrames;
    private bool _capture;

    public override void _Ready()
    {
        _capture = OS.GetCmdlineUserArgs().Contains("--capture");
        _font = new SystemFont { FontNames = ["Microsoft YaHei UI", "Arial"] };
        if (OS.GetCmdlineUserArgs().Contains("--self-test") || OS.GetCmdlineUserArgs().Contains("--member-acceptance") || OS.GetCmdlineUserArgs().Contains("--recovery-gate"))
        {
            SetProcess(false);
            try
            {
                if (OS.GetCmdlineUserArgs().Contains("--recovery-gate"))
                {
                    RouteRecoveryChecks.Frozen(line => GD.Print(line));
                    GetTree().Quit();
                    return;
                }
                var passed = OS.GetCmdlineUserArgs().Contains("--member-acceptance")
                    ? RouteMemberChecks.Run(line => GD.Print(line)) : RouteCheckSuite.Run(line => GD.Print(line));
                GetTree().Quit(passed ? 0 : 1);
            }
            catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
            return;
        }
        var prepare = Stopwatch.StartNew();
        RoutePreparation.Run();
        prepare.Stop();
        GD.Print($"[Route preparation] elapsed_ms={prepare.Elapsed.TotalMilliseconds:R}");
        Reset(4);
        GD.Print($"[Route prototype] snapshot_format={RouteSettings.SnapshotFormat}; independent experimental world; production RtsMatch/v14 unchanged");
    }

    private void Reset(int scenario)
    {
        _world = RouteScenarios.Create(scenario);
        _scenario = scenario;
        _selected.Clear();
        foreach (var body in _world.Units.Where(body => body.Phase != WalkPhase.Stopped)) _selected.Add(body.Id);
        _accumulator = 0;
        _peak = 0;
        _replayStart = -1;
        _saved = "";
        _message = "新规则实验；按R重放五次往返，F2显示/隐藏参考路线";
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_world is null) return;
        if (_capture && ++_renderedFrames == 60)
        {
            var image = GetViewport().GetTexture().GetImage();
            var path = OS.GetCmdlineUserArgs().First(argument => argument.StartsWith("--capture-path="))[15..];
            var error = image.SavePng(path);
            GD.Print($"[Route UI capture] {path} result={error} frame={_world.Frame}");
            GetTree().Quit(error == Error.Ok ? 0 : 1);
            return;
        }
        if (!_paused)
        {
            _accumulator += delta;
            var steps = 0;
            while (_accumulator >= 1.0 / 30 && steps++ < 8)
            {
                Advance();
                _accumulator -= 1.0 / 30;
            }
        }
        QueueRedraw();
    }

    private void Advance()
    {
        if (_replayStart >= 0 && _replayIndex < 5 && _world.Frame - _replayStart >= _replayIndex * 31)
        {
            _world.Move(_world.Units.Where(body => body.Phase != WalkPhase.Stopped).Select(body => body.Id),
                _replayIndex % 2 == 0 ? new(1550, 640) : new(450, 640));
            _replayIndex++;
        }
        var watch = Stopwatch.StartNew();
        _world.Step();
        watch.Stop();
        _peak = Math.Max(_peak, watch.Elapsed.TotalMilliseconds);
        foreach (var item in _world.Events.Where(item => item.Kind is "failed" or "rejected" or "retry"))
        {
            _message = $"#{item.Id}: {item.Kind} {item.Detail}";
            GD.Print($"[Route event] frame={item.Frame} id={item.Id} {item.Kind} {item.Detail}");
        }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key5)
            {
                Reset((int)key.Keycode - (int)Key.Key1 + 1);
                return;
            }
            switch (key.Keycode)
            {
                case Key.F2: _routes = !_routes; break;
                case Key.F5: _saved = _world.Save(); _message = "实验快照已保存"; break;
                case Key.F9:
                    if (_saved.Length > 0)
                    {
                        try { _world = RouteWorld.Load(_saved); _accumulator = 0; _replayStart = -1; _message = "已恢复实验快照"; }
                        catch (Exception error) when (error is InvalidDataException or ArgumentException or System.Text.Json.JsonException)
                        { _message = error.Message; }
                    }
                    break;
                case Key.Space: _world.Stop(_selected); _replayStart = -1; break;
                case Key.P: _paused = !_paused; _accumulator = 0; break;
                case Key.N: if (_paused) Advance(); break;
                case Key.A:
                    _selected.Clear();
                    foreach (var body in _world.Units) _selected.Add(body.Id);
                    break;
                case Key.R: _replayStart = _world.Frame; _replayIndex = 0; break;
            }
        }
        if (input is not InputEventMouseButton mouse) return;
        var point = ToWorld(mouse.Position);
        if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
        {
            _replayStart = -1;
            if (!_world.Move(_selected, point, mouse.ShiftPressed)) _message = "命令被拒绝，原订单保留";
        }
        else if (mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed) _drag = mouse.Position;
            else if (_drag is { } start)
            {
                if (!mouse.ShiftPressed) _selected.Clear();
                var rectangle = new Rect2(start, mouse.Position - start).Abs();
                if (rectangle.Size.Length() < 6)
                {
                    var picked = _world.Units.OrderBy(body => (body.Position - point).Length).FirstOrDefault();
                    if (picked is not null && (picked.Position - point).Length <= 40) _selected.Add(picked.Id);
                }
                else foreach (var body in _world.Units)
                    if (rectangle.HasPoint(ToScreen(body.Position))) _selected.Add(body.Id);
                _drag = null;
            }
        }
    }

    private static Vector2 ToScreen(FixedVector2 point) => MapOffset + new Vector2(
        (float)point.X.ToPresentationDouble(), (float)point.Y.ToPresentationDouble()) * MapScale;
    private static FixedVector2 ToWorld(Vector2 point)
    {
        var world = (point - MapOffset) / MapScale;
        return new((int)MathF.Round(world.X), (int)MathF.Round(world.Y));
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, new Vector2(1360, 960)), new Color("101923"));
        if (_world is null || _font is null) return;
        Text(24, "统一路线参考实验    1 双人跟随  2 迎面相遇  3 Stop挡路  4 窄路24人  5 空地24人", 19);
        Text(50, "左键点选/拖框 | A全选 | 右键移动 | Shift+右键追加 | Space停止 | F2路线 | F5/F9存读档", 16);
        Text(74, $"P暂停 / N单步 | R五次往返   场景{_scenario}  帧{_world.Frame}  活动{_world.Units.Count(body => body.Task != 0)}  失败{_world.Units.Count(body => body.Phase == WalkPhase.Failed)}  峰值{_peak:F2}ms  {_message}", 15);
        Text(97, "尚未通过24人拥挤验收；青线=参考路线，橙色=等待，红色=失败；参数：推进32 / 等待90帧 / 重试3轮", 14);
        var flags = _world.ReadFlags();
        for (var y = 0; y < _world.Height; y++)
        for (var x = 0; x < _world.Width; x++)
        {
            var rectangle = new Rect2(MapOffset + new Vector2(x, y) * 32 * MapScale, Vector2.One * 32 * MapScale);
            DrawRect(rectangle, flags[y * _world.Width + x] != 0 ? new Color("766b59") : new Color("203c33"));
            DrawRect(rectangle, new Color(0.2f, 0.32f, 0.3f, 0.2f), filled: false);
        }
        if (_routes)
        {
            foreach (var path in _world.Paths)
                for (var i = 1; i < path.Points.Count; i++)
                    DrawLine(ToScreen(path.Points[i - 1]), ToScreen(path.Points[i]), new Color(0.3f, 0.8f, 1, 0.7f), 2);
            foreach (var body in _world.Units.Where(body => body.Task != 0))
            {
                var point = ToScreen(body.Preference);
                DrawLine(point - new Vector2(5, 0), point + new Vector2(5, 0), Colors.LightGreen);
                DrawLine(point - new Vector2(0, 5), point + new Vector2(0, 5), Colors.LightGreen);
            }
        }
        foreach (var body in _world.Units)
        {
            var point = ToScreen(body.Position);
            var color = body.Phase switch
            {
                WalkPhase.Stopped => Colors.Gray, WalkPhase.Waiting => Colors.Orange,
                WalkPhase.Failed => Colors.IndianRed, WalkPhase.Settled => Colors.LightGreen,
                _ => new Color("7dd3eb")
            };
            DrawCircle(point, 31 * MapScale, color);
            DrawArc(point, 31 * MapScale + 2, 0, Mathf.Tau, 24, _selected.Contains(body.Id) ? Colors.Yellow : Colors.Black, 2);
            var heading = Vector2.FromAngle((float)body.Facing.ToPresentationDouble());
            DrawLine(point, point + heading * 31 * MapScale, Colors.White, 2);
            DrawString(_font, point + new Vector2(-7, 5), body.Id.ToString(), fontSize: 12, modulate: Colors.Black);
        }
        if (_drag is { } drag) DrawRect(new Rect2(drag, GetViewport().GetMousePosition() - drag).Abs(), Colors.Yellow, filled: false);
    }

    private void Text(int y, string text, int size) => DrawString(_font, new Vector2(24, y), text, fontSize: size, modulate: Colors.White);
}
