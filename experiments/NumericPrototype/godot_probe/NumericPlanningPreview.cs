using Godot;
using NumericPrototype;
using System.Globalization;

public partial class NumericPlanningPreview : Node2D
{
    private const double RawScale = 4294967296.0;
    private PlanningPreviewSession _session = null!;
    private PreviewMap _map = null!;
    private PreviewUnit[] _units = [];
    private ImageTexture _texture = null!;
    private Label _status = null!;
    private Label _notice = null!;
    private OptionButton _formation = null!;
    private string _assets = "";
    private string? _saved;
    private int _savedCount;
    private PreviewPoint? _savedGoal;
    private int _savedFormation;
    private bool _paused;
    private bool _links;
    private float _zoom = 1;
    private Vector2 _pan;
    private PreviewPoint? _goal;
    private int _frames;
    private bool _selftest;
    private string? _capture;
    private bool _capturing;

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            var index = Array.IndexOf(args, "--assets");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Launch with --assets <directory>.");
            _assets = args[index + 1];
            _selftest = args.Contains("--preview-selftest");
            var captureIndex = Array.IndexOf(args, "--preview-capture");
            if (captureIndex >= 0 && captureIndex + 1 < args.Length) _capture = args[captureIndex + 1];
            _status = GetNode<Label>("Hud/Panel/Layout/Status");
            _notice = GetNode<Label>("Hud/Panel/Layout/Notice");
            _formation = GetNode<OptionButton>("Hud/Panel/Layout/Toolbar/Formation");
            foreach (var text in new[] { "紧凑", "矩形", "楔形", "圆形" }) _formation.AddItem(text);
            foreach (var count in new[] { 65, 300, 500 })
            {
                var captured = count;
                GetNode<Button>($"Hud/Panel/Layout/Toolbar/Count{count}").Pressed += () => Guard(() => Reset(captured));
            }
            _formation.ItemSelected += _ => Guard(() => Plan(_goal ?? Center()));
            GetNode<Button>("Hud/Panel/Layout/Toolbar/Pause").Pressed += () => _paused = !_paused;
            GetNode<Button>("Hud/Panel/Layout/Toolbar/Save").Pressed += Save;
            GetNode<Button>("Hud/Panel/Layout/Toolbar/Load").Pressed += () => Guard(Load);
            GetNode<Button>("Hud/Panel/Layout/Toolbar/Reset").Pressed += () => Guard(() => Reset(_session.Count));
            foreach (var button in GetNode<HBoxContainer>("Hud/Panel/Layout/Toolbar").GetChildren().OfType<BaseButton>())
                button.FocusMode = Control.FocusModeEnum.None;
            Reset(_selftest ? 500 : 300);
            GD.Print("PLANNING_PREVIEW_READY members=" + _session.Count);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    public override void _ExitTree() => _session?.Dispose();

    private void Reset(int count)
    {
        var session = new PlanningPreviewSession(_assets, count);
        _session?.Dispose();
        _session = session;
        _map = _session.ReadMap();
        var pixels = new byte[_map.Width * _map.Height * 4];
        for (var index = 0; index < _map.Walkable.Length; index++)
        {
            var open = _map.Walkable[index] != 0;
            pixels[index * 4] = open ? (byte)30 : (byte)15;
            pixels[index * 4 + 1] = open ? (byte)60 : (byte)24;
            pixels[index * 4 + 2] = open ? (byte)52 : (byte)35;
            pixels[index * 4 + 3] = 255;
        }
        _texture = ImageTexture.CreateFromImage(Image.CreateFromData(_map.Width, _map.Height, false, Image.Format.Rgba8, pixels));
        _goal = Center();
        _notice.Text = "蓝点：固定起点  绿圈：成功落点  红叉：失败  橙点：取消；这是落点规划验收，不推进群体移动。";
        Plan(_goal);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_session is null) return;
        Guard(() =>
        {
            if (!_paused) _session.Tick();
            _units = _session.ReadUnits();
            var info = _session.Metrics();
            _status.Text = $"{_session.Count}成员 | {(info.Planning ? "规划中" : "已完成")} | 帧 {info.Frame} | 候选 {info.Prepared} | 匹配行 {info.MatchingRow} | 成功 {info.Succeeded} / 失败 {info.Failed} | {(_paused ? "已暂停" : "运行中")}\n"
                + $"创建 {info.CreateMs:F1}ms · 逻辑步峰值 {info.PeakTickMs:F1}ms · 落点累计 {info.PlacementMs:F1}ms / 峰值 {info.PlacementPeakMs:F1}ms · 分配 {info.AllocatedBytes / 1048576.0:F1}MB（不含素材/绘制，不代表性能达标）";
            if (_selftest && ++_frames == 1) { Save(); _session.Tick(); Load(); }
            if (_selftest && !_session.Planning && !_capturing)
            {
                if (info.Succeeded + info.Failed != 500) throw new InvalidOperationException("Preview lost planning members.");
                GD.Print("PLANNING_PREVIEW_SELFTEST_PASS succeeded=" + info.Succeeded + " failed=" + info.Failed);
                if (_capture is null) GetTree().Quit();
                else { _capturing = true; CaptureFrame(); }
            }
            QueueRedraw();
        });
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (_session is null) return;
        Guard(() =>
        {
            if (input is InputEventMouseButton mouse && mouse.Pressed)
            {
                if (mouse.ButtonIndex == MouseButton.WheelUp) _zoom = Math.Min(4, _zoom * 1.2f);
                else if (mouse.ButtonIndex == MouseButton.WheelDown) _zoom = Math.Max(0.5f, _zoom / 1.2f);
                else if (mouse.ButtonIndex == MouseButton.Left && mouse.Position.Y > 210)
                {
                    var local = (mouse.Position - MapOffset()) / WorldScale();
                    Plan(new PreviewPoint(checked(_map.Origin.X + (long)(local.X * RawScale)),
                        checked(_map.Origin.Y + (long)(local.Y * RawScale))));
                }
                else if (mouse.ButtonIndex == MouseButton.Right && _session.Planning)
                {
                    var nearest = _units.OrderBy(unit => Screen(unit.Start).DistanceSquaredTo(mouse.Position)).First();
                    if (Screen(nearest.Start).DistanceTo(mouse.Position) < 18) _session.Cancel(nearest.Id);
                }
            }
            else if (input is InputEventMouseMotion motion && Input.IsMouseButtonPressed(MouseButton.Middle)) _pan += motion.Relative;
            else if (input is InputEventKey key && key.Pressed && !key.Echo)
            {
                if (key.Keycode == Key.Space) _paused = !_paused;
                else if (key.Keycode == Key.F5) Save();
                else if (key.Keycode == Key.F9) Load();
                else if (key.Keycode == Key.L) _links = !_links;
                else if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key4)
                {
                    _formation.Select((int)key.Keycode - (int)Key.Key1);
                    Plan(_goal ?? Center());
                }
            }
            QueueRedraw();
        });
    }

    public override void _Draw()
    {
        if (_session is null) return;
        DrawTextureRect(_texture, new Rect2(MapOffset(), new Vector2(_map.Width, _map.Height) * (float)(_map.CellSize / RawScale) * WorldScale()), false);
        var radius = Math.Max(2, (float)(_session.BodyRadius / RawScale) * WorldScale());
        foreach (var unit in _units)
        {
            var start = Screen(unit.Start);
            DrawCircle(start, radius, unit.Canceled ? Colors.Orange : Colors.DeepSkyBlue);
            if (unit.Id == _session.Leader) DrawArc(start, radius + 2, 0, Mathf.Tau, 16, Colors.Gold, 1);
            if (unit.Goal is not null)
            {
                var goal = Screen(unit.Goal);
                if (_links) DrawLine(start, goal, new Color(0.3f, 0.8f, 0.6f, 0.2f));
                DrawArc(goal, radius, 0, Mathf.Tau, 16, Colors.LightGreen, 1);
            }
            if (unit.Failed)
            {
                DrawLine(start - new Vector2(4, 4), start + new Vector2(4, 4), Colors.Tomato, 2);
                DrawLine(start + new Vector2(-4, 4), start + new Vector2(4, -4), Colors.Tomato, 2);
            }
        }
        if (_goal is not null) DrawArc(Screen(_goal), 9, 0, Mathf.Tau, 24, Colors.White, 2);
    }

    private async void CaptureFrame()
    {
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            var result = image.SavePng(_capture!);
            if (result != Error.Ok) throw new IOException("Cannot save viewport: " + result);
            GD.Print("PLANNING_PREVIEW_CAPTURE_PASS");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private PreviewPoint Center() => new(_map.Origin.X + _map.Width * _map.CellSize / 2, _map.Origin.Y + _map.Height * _map.CellSize / 2);
    private Vector2 MapOffset() => new Vector2(20, 210) + _pan;
    private float WorldScale() => Math.Min((GetViewportRect().Size.X - 40) / (float)(_map.Width * _map.CellSize / RawScale),
        (GetViewportRect().Size.Y - 230) / (float)(_map.Height * _map.CellSize / RawScale)) * _zoom;
    private Vector2 Screen(PreviewPoint value) => MapOffset() + new Vector2((float)((value.X - _map.Origin.X) / RawScale),
        (float)((value.Y - _map.Origin.Y) / RawScale)) * WorldScale();
    private void Plan(PreviewPoint point)
    {
        _session.Plan(_formation.Selected, (point.X / (decimal)RawScale).ToString(CultureInfo.InvariantCulture),
            (point.Y / (decimal)RawScale).ToString(CultureInfo.InvariantCulture));
        _goal = point;
    }
    private void Save() { _saved = _session.Capture(); _savedCount = _session.Count; _savedGoal = _goal; _savedFormation = _formation.Selected; _notice.Text = "已保存内存快照，可继续操作后按F9恢复。"; }
    private void Load()
    {
        if (_saved is null || _savedCount != _session.Count)
        {
            _notice.Text = "请先保存相同人数的快照。";
            return;
        }
        _session.Restore(_saved);
        _goal = _savedGoal;
        _formation.Select(_savedFormation);
        _notice.Text = "快照已恢复，继续相同的固定工作量规划；计时诊断已清零。";
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception error) when (!_selftest && error is ArgumentException or InvalidDataException)
        {
            _notice.Text = error.Message;
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            if (_selftest) GetTree().Quit(1);
            else { _paused = true; _notice.Text = "运行异常已暂停，详见日志：" + error.Message; }
        }
    }
}
