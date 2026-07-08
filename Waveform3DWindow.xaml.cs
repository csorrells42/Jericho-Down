using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using JerichoDown.Audio;

namespace JerichoDown;

public partial class Waveform3DWindow : Window
{
    private const int HistoryDepth = 48;
    private const int PointCount = 112;
    private const double XWidth = 8.6d;
    private const double ZDepth = 5.8d;
    private const double FloorY = -1.42d;
    private const double FarHistoryX = -7.5d;
    private const double NearHistoryX = 4.65d;
    private const double MinimumHistoryScale = 0.13d;
    private const double CameraHistoryLookBack = 3.25d;
    private readonly List<double[]> _history = [];
    private readonly MeshGeometry3D _surfaceMesh = new();
    private readonly MeshGeometry3D _waveLineMesh = new();
    private readonly MeshGeometry3D _waveGlowMesh = new();
    private readonly MeshGeometry3D _reflectionMesh = new();
    private readonly Dictionary<int, Int32Collection> _surfaceTriangleIndices = [];
    private readonly Dictionary<int, Int32Collection> _ribbonTriangleIndices = [];
    private SpectrumFrame? _pendingFrame;
    private int _hasPendingFrame;
    private DateTime _lastStatusUpdateUtc = DateTime.MinValue;
    private bool _lastStatusWasWaveform = true;
    private int _lastStatusPeakPercent = -1;

    public Waveform3DWindow()
    {
        InitializeComponent();
        SceneModel.Children.Add(CreateFloorModel());
        SceneModel.Children.Add(CreateWaveModel(_reflectionMesh, CreateReflectionMaterial()));
        SceneModel.Children.Add(CreateGridModel());
        SceneModel.Children.Add(CreateWaveModel(_surfaceMesh, CreateSurfaceMaterial()));
        SceneModel.Children.Add(CreateWaveModel(_waveGlowMesh, CreateNeonMaterial(0.34d, 0.58d)));
        SceneModel.Children.Add(CreateWaveModel(_waveLineMesh, CreateNeonMaterial(0.98d, 0.42d)));
        UpdateOrbitCamera();
        CompositionTarget.Rendering += CompositionTargetRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= CompositionTargetRendering;
    }

    public void AcceptFrame(SpectrumFrame frame)
    {
        Interlocked.Exchange(ref _pendingFrame, frame);
        Interlocked.Exchange(ref _hasPendingFrame, 1);
    }

    private void CompositionTargetRendering(object? sender, EventArgs e)
    {
        if (!WaveformViewport.IsVisible)
        {
            return;
        }

        if (Interlocked.Exchange(ref _hasPendingFrame, 0) == 0)
        {
            return;
        }

        var frame = Interlocked.Exchange(ref _pendingFrame, null);
        if (frame is null)
        {
            return;
        }

        var sourceSamples = frame.ProcessedSamples.Length > 0
            ? frame.ProcessedSamples
            : frame.RawSamples;
        var isWaveform = sourceSamples.Length > 0;
        var slice = isWaveform
            ? CreateWaveformSlice(sourceSamples)
            : CreateSpectrumSlice(frame.Magnitudes);

        _history.Add(slice);
        while (_history.Count > HistoryDepth)
        {
            _history.RemoveAt(0);
        }

        UpdateStatusText(isWaveform, frame.PeakLevel);
        RenderMesh();
    }

    private void UpdateStatusText(bool isWaveform, double peakLevel)
    {
        var now = DateTime.UtcNow;
        var peakPercent = (int)Math.Round(Math.Clamp(peakLevel, 0d, 1d) * 100d);
        if (isWaveform == _lastStatusWasWaveform
            && peakPercent == _lastStatusPeakPercent
            && now - _lastStatusUpdateUtc < TimeSpan.FromMilliseconds(200))
        {
            return;
        }

        _lastStatusWasWaveform = isWaveform;
        _lastStatusPeakPercent = peakPercent;
        _lastStatusUpdateUtc = now;
        ModeText.Text = isWaveform ? "Processed waveform" : "Processed spectrum";
        PeakText.Text = $"Peak {peakPercent}%";
    }

    private void CameraControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateOrbitCamera();
    }

    private void UpdateOrbitCamera()
    {
        if (OrbitCamera is null || OrbitSlider is null || DistanceSlider is null || HeightSlider is null)
        {
            return;
        }

        var orbitRadians = OrbitSlider.Value * Math.PI / 180d;
        var distance = Math.Clamp(DistanceSlider.Value, DistanceSlider.Minimum, DistanceSlider.Maximum);
        var height = Math.Clamp(HeightSlider.Value, HeightSlider.Minimum, HeightSlider.Maximum);
        var pivot = new Point3D(NearHistoryX, 0d, 0d);
        var cameraPosition = new Point3D(
            pivot.X + Math.Cos(orbitRadians) * distance,
            height,
            pivot.Z + Math.Sin(orbitRadians) * distance);
        var lookTarget = new Point3D(NearHistoryX - CameraHistoryLookBack, -0.2d, 0d);

        OrbitCamera.Position = cameraPosition;
        OrbitCamera.LookDirection = lookTarget - cameraPosition;
        OrbitCamera.UpDirection = new Vector3D(0d, 1d, 0d);
    }

    private void RenderMesh()
    {
        var visibleRows = Math.Min(_history.Count, HistoryDepth);
        if (visibleRows == 0)
        {
            return;
        }

        var meshRows = Math.Max(2, visibleRows);
        PopulateSurfaceMesh(_surfaceMesh, meshRows, visibleRows, yScale: 1.72d);
        PopulateRibbonMesh(_waveGlowMesh, meshRows, visibleRows, thickness: 0.064d, yScale: 1.92d, mirror: false);
        PopulateRibbonMesh(_waveLineMesh, meshRows, visibleRows, thickness: 0.022d, yScale: 1.88d, mirror: false);
        PopulateRibbonMesh(_reflectionMesh, meshRows, visibleRows, thickness: 0.024d, yScale: 0.56d, mirror: true);
    }

    private void PopulateSurfaceMesh(MeshGeometry3D mesh, int meshRows, int visibleRows, double yScale)
    {
        var pointTotal = meshRows * PointCount;
        var positions = EnsurePoint3DCollection(mesh, pointTotal);
        var textureCoordinates = EnsureTextureCoordinateCollection(mesh, pointTotal);
        var writeIndex = 0;

        for (var row = 0; row < meshRows; row++)
        {
            var sourceRow = visibleRows == 1
                ? _history[^1]
                : _history[_history.Count - visibleRows + Math.Min(row, visibleRows - 1)];
            var age = meshRows == 1 ? 1d : row / (double)(meshRows - 1);
            var perspectiveAge = GetPerspectiveAge(age);
            var historyScale = GetHistoryScale(perspectiveAge);
            var x = GetHistoryX(perspectiveAge);
            var edgeLift = Math.Sin(perspectiveAge * Math.PI) * 0.04d * historyScale;

            for (var point = 0; point < PointCount; point++)
            {
                var samplePosition = point / (double)(PointCount - 1);
                var z = ZDepth * (samplePosition - 0.5d) * historyScale;
                var loudness = GetLoudness(sourceRow[point]);
                var y = sourceRow[point] * yScale * historyScale + edgeLift - 0.02d;
                var textureX = GetTexturePosition(perspectiveAge, loudness);
                positions[writeIndex] = new Point3D(x, y, z);
                textureCoordinates[writeIndex] = new Point(textureX, loudness);
                writeIndex++;
            }
        }

        mesh.Positions = positions;
        mesh.TextureCoordinates = textureCoordinates;
        mesh.TriangleIndices = GetSurfaceTriangleIndices(meshRows);
    }

    private void PopulateRibbonMesh(MeshGeometry3D mesh, int meshRows, int visibleRows, double thickness, double yScale, bool mirror)
    {
        var pointTotal = meshRows * PointCount * 2;
        var positions = EnsurePoint3DCollection(mesh, pointTotal);
        var textureCoordinates = EnsureTextureCoordinateCollection(mesh, pointTotal);
        var writeIndex = 0;

        for (var row = 0; row < meshRows; row++)
        {
            var sourceRow = visibleRows == 1
                ? _history[^1]
                : _history[_history.Count - visibleRows + Math.Min(row, visibleRows - 1)];
            var age = meshRows == 1 ? 1d : row / (double)(meshRows - 1);
            var perspectiveAge = GetPerspectiveAge(age);
            var historyScale = GetHistoryScale(perspectiveAge);
            var x = GetHistoryX(perspectiveAge);
            var edgeLift = Math.Sin(perspectiveAge * Math.PI) * 0.04d * historyScale;

            for (var point = 0; point < PointCount; point++)
            {
                var samplePosition = point / (double)(PointCount - 1);
                var z = ZDepth * (samplePosition - 0.5d) * historyScale;
                var loudness = GetLoudness(sourceRow[point]);
                var y = sourceRow[point] * yScale * historyScale + edgeLift;
                if (mirror)
                {
                    y = FloorY - (y - FloorY) * 0.42d * historyScale - 0.04d;
                }

                var halfThickness = thickness * historyScale * (1d + loudness * 2.35d);
                var textureX = GetTexturePosition(perspectiveAge, loudness);
                positions[writeIndex] = new Point3D(x, y + halfThickness, z);
                textureCoordinates[writeIndex] = new Point(textureX, loudness);
                writeIndex++;
                positions[writeIndex] = new Point3D(x, y - halfThickness, z);
                textureCoordinates[writeIndex] = new Point(textureX, loudness);
                writeIndex++;
            }
        }

        mesh.Positions = positions;
        mesh.TextureCoordinates = textureCoordinates;
        mesh.TriangleIndices = GetRibbonTriangleIndices(meshRows);
    }

    private static Point3DCollection EnsurePoint3DCollection(MeshGeometry3D mesh, int count)
    {
        var collection = mesh.Positions;
        if (collection is not null && !collection.IsFrozen && collection.Count == count)
        {
            return collection;
        }

        collection = new Point3DCollection(count);
        for (var i = 0; i < count; i++)
        {
            collection.Add(default);
        }

        mesh.Positions = collection;
        return collection;
    }

    private static PointCollection EnsureTextureCoordinateCollection(MeshGeometry3D mesh, int count)
    {
        var collection = mesh.TextureCoordinates;
        if (collection is not null && !collection.IsFrozen && collection.Count == count)
        {
            return collection;
        }

        collection = new PointCollection(count);
        for (var i = 0; i < count; i++)
        {
            collection.Add(default);
        }

        mesh.TextureCoordinates = collection;
        return collection;
    }

    private Int32Collection GetSurfaceTriangleIndices(int meshRows)
    {
        if (_surfaceTriangleIndices.TryGetValue(meshRows, out var cached))
        {
            return cached;
        }

        var triangleIndices = new Int32Collection((meshRows - 1) * (PointCount - 1) * 6);
        for (var row = 0; row < meshRows - 1; row++)
        {
            var rowOffset = row * PointCount;
            var nextRowOffset = (row + 1) * PointCount;
            for (var point = 0; point < PointCount - 1; point++)
            {
                var a = rowOffset + point;
                var b = rowOffset + point + 1;
                var c = nextRowOffset + point;
                var d = nextRowOffset + point + 1;
                triangleIndices.Add(a);
                triangleIndices.Add(c);
                triangleIndices.Add(b);
                triangleIndices.Add(b);
                triangleIndices.Add(c);
                triangleIndices.Add(d);
            }
        }

        FreezeCollection(triangleIndices);
        _surfaceTriangleIndices[meshRows] = triangleIndices;
        return triangleIndices;
    }

    private Int32Collection GetRibbonTriangleIndices(int meshRows)
    {
        if (_ribbonTriangleIndices.TryGetValue(meshRows, out var cached))
        {
            return cached;
        }

        var triangleIndices = new Int32Collection(meshRows * (PointCount - 1) * 6);
        for (var row = 0; row < meshRows; row++)
        {
            var rowOffset = row * PointCount * 2;
            for (var point = 0; point < PointCount - 1; point++)
            {
                var a = rowOffset + point * 2;
                var b = a + 1;
                var c = a + 2;
                var d = a + 3;
                triangleIndices.Add(a);
                triangleIndices.Add(b);
                triangleIndices.Add(c);
                triangleIndices.Add(c);
                triangleIndices.Add(b);
                triangleIndices.Add(d);
            }
        }

        FreezeCollection(triangleIndices);
        _ribbonTriangleIndices[meshRows] = triangleIndices;
        return triangleIndices;
    }

    private static void FreezeCollection(Freezable collection)
    {
        if (collection.CanFreeze)
        {
            collection.Freeze();
        }
    }

    private static GeometryModel3D CreateWaveModel(MeshGeometry3D mesh, Material material)
    {
        var model = new GeometryModel3D(mesh, material)
        {
            BackMaterial = material
        };
        return model;
    }

    private static Material CreateNeonMaterial(double diffuseOpacity, double emissiveOpacity)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(CreateNeonBrush(diffuseOpacity)));
        material.Children.Add(new EmissiveMaterial(CreateNeonBrush(emissiveOpacity)));
        material.Children.Add(new SpecularMaterial(CreateBrush(Color.FromArgb(210, 235, 252, 255)), 34d));
        return material;
    }

    private static Material CreateSurfaceMaterial()
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(CreateNeonBrush(0.42d)));
        material.Children.Add(new EmissiveMaterial(CreateNeonBrush(0.16d)));
        material.Children.Add(new SpecularMaterial(CreateBrush(Color.FromArgb(115, 216, 244, 255)), 18d));
        return material;
    }

    private static Material CreateReflectionMaterial()
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(CreateNeonBrush(0.17d)));
        material.Children.Add(new EmissiveMaterial(CreateNeonBrush(0.12d)));
        return material;
    }

    private static GeometryModel3D CreateFloorModel()
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new(-4.8d, FloorY - 0.025d, 3.25d),
                new(4.8d, FloorY - 0.025d, 3.25d),
                new(-4.8d, FloorY - 0.025d, -3.25d),
                new(4.8d, FloorY - 0.025d, -3.25d)
            },
            TriangleIndices = new Int32Collection { 0, 2, 1, 1, 2, 3 }
        };

        return new GeometryModel3D(mesh, new DiffuseMaterial(CreateBrush(Color.FromArgb(126, 3, 9, 14))));
    }

    private static GeometryModel3D CreateGridModel()
    {
        var mesh = new MeshGeometry3D();
        var positions = new Point3DCollection();
        var triangles = new Int32Collection();

        for (var i = 0; i <= 10; i++)
        {
            var x = -4.5d + i * 0.9d;
            AddFloorStrip(positions, triangles, new Point3D(x, FloorY, -3.1d), new Point3D(x, FloorY, 3.1d), 0.010d);
        }

        for (var i = 0; i <= 8; i++)
        {
            var z = -3.1d + i * 0.775d;
            AddFloorStrip(positions, triangles, new Point3D(-4.5d, FloorY + 0.01d, z), new Point3D(4.5d, FloorY + 0.01d, z), 0.010d);
        }

        mesh.Positions = positions;
        mesh.TriangleIndices = triangles;
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(CreateBrush(Color.FromArgb(110, 17, 98, 122))));
        material.Children.Add(new EmissiveMaterial(CreateBrush(Color.FromArgb(82, 0, 206, 238))));
        return new GeometryModel3D(mesh, material);
    }

    private static void AddFloorStrip(Point3DCollection positions, Int32Collection triangles, Point3D start, Point3D end, double width)
    {
        var index = positions.Count;
        var horizontal = Math.Abs(start.X - end.X) > Math.Abs(start.Z - end.Z);
        if (horizontal)
        {
            positions.Add(new Point3D(start.X, start.Y, start.Z - width));
            positions.Add(new Point3D(end.X, end.Y, end.Z - width));
            positions.Add(new Point3D(start.X, start.Y, start.Z + width));
            positions.Add(new Point3D(end.X, end.Y, end.Z + width));
        }
        else
        {
            positions.Add(new Point3D(start.X - width, start.Y, start.Z));
            positions.Add(new Point3D(end.X - width, end.Y, end.Z));
            positions.Add(new Point3D(start.X + width, start.Y, start.Z));
            positions.Add(new Point3D(end.X + width, end.Y, end.Z));
        }

        triangles.Add(index);
        triangles.Add(index + 2);
        triangles.Add(index + 1);
        triangles.Add(index + 1);
        triangles.Add(index + 2);
        triangles.Add(index + 3);
    }

    private static double[] CreateWaveformSlice(float[] samples)
    {
        var slice = new double[PointCount];
        if (samples.Length == 0)
        {
            return slice;
        }

        for (var point = 0; point < PointCount; point++)
        {
            var start = point * samples.Length / PointCount;
            var end = Math.Max(start + 1, (point + 1) * samples.Length / PointCount);
            end = Math.Min(end, samples.Length);
            var maxAbs = 0d;
            var signedPeak = 0d;
            for (var i = start; i < end; i++)
            {
                var sample = Math.Clamp(samples[i], -1f, 1f);
                var abs = Math.Abs(sample);
                if (abs > maxAbs)
                {
                    maxAbs = abs;
                    signedPeak = sample;
                }
            }

            slice[point] = Math.Tanh(signedPeak * 2.35d);
        }

        return slice;
    }

    private static double[] CreateSpectrumSlice(double[] magnitudes)
    {
        var slice = new double[PointCount];
        if (magnitudes.Length == 0)
        {
            return slice;
        }

        for (var point = 0; point < PointCount; point++)
        {
            var sourceIndex = point * (magnitudes.Length - 1) / Math.Max(1, PointCount - 1);
            var magnitude = Math.Clamp(magnitudes[sourceIndex], 0d, 1d);
            slice[point] = Math.Pow(magnitude, 0.48d) * 1.34d;
        }

        return slice;
    }

    private static double GetLoudness(double sample)
    {
        return Math.Clamp(Math.Pow(Math.Abs(sample) * 2d, 0.58d), 0d, 1d);
    }

    private static double GetPerspectiveAge(double age)
    {
        return Math.Pow(Math.Clamp(age, 0d, 1d), 1.9d);
    }

    private static double GetHistoryScale(double perspectiveAge)
    {
        return MinimumHistoryScale + (1d - MinimumHistoryScale) * perspectiveAge;
    }

    private static double GetHistoryX(double perspectiveAge)
    {
        return FarHistoryX + (NearHistoryX - FarHistoryX) * perspectiveAge;
    }

    private static double GetTexturePosition(double perspectiveAge, double loudness)
    {
        return Math.Clamp(0.01d + perspectiveAge * 0.48d + loudness * 0.50d, 0d, 1d);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private static LinearGradientBrush CreateNeonBrush(double opacity)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5d),
            EndPoint = new Point(1d, 0.5d),
            Opacity = Math.Clamp(opacity, 0d, 1d)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 54, 18), 0d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 152, 36), 0.10d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 222, 62), 0.19d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 72, 210), 0.31d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(185, 77, 255), 0.44d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(88, 83, 255), 0.56d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 128, 255), 0.66d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0, 220, 255), 0.78d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(72, 255, 190), 0.90d));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(154, 255, 108), 1d));
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
