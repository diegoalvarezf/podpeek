#Requires -Version 5.1
<#
.SYNOPSIS
    PodPeek (versión script): la batería y los gestos de tus AirPods en Windows, con estilo de macOS.
.DESCRIPTION
    - Batería de cada auricular y del estuche en una ventanita como la del iPhone.
    - Pausa la música al quitarte un auricular y la reanuda al volver a ponértelo.
    - Conectar con un clic y ponerlos como predeterminados (también para Teams) al conectarse.
    Todo local: escucha el anuncio Bluetooth de los AirPods y usa el audio de Windows. Sin internet.
.NOTES
    Necesita Windows PowerShell 5.1 (powershell.exe), no PowerShell 7: usa las APIs WinRT de Windows.
    Oculto: powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File podpeek.ps1 -Background
#>
param([switch]$Background)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Windows.Forms, System.Drawing, System.Runtime.WindowsRuntime

# ─── Instancia única ────────────────────────────────────────────────────────
$script:mutex = New-Object System.Threading.Mutex($false, 'PodPeek.Script.SingleInstance')
if (-not $script:mutex.WaitOne(0, $false)) { exit 0 }

# ─── Rutas, registro y ajustes ──────────────────────────────────────────────
$script:Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:LogDir = Join-Path $env:LOCALAPPDATA 'PodPeek'
$script:LogFile = Join-Path $script:LogDir 'podpeek-script.log'
$script:SettingsFile = Join-Path $env:APPDATA 'PodPeek\settings.json'
$script:StartupVbs = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\PodPeek.vbs'
New-Item -ItemType Directory -Force -Path $script:LogDir | Out-Null

function Write-Log([string]$Message) {
    try {
        if ((Test-Path $script:LogFile) -and (Get-Item $script:LogFile).Length -gt 1MB) { Remove-Item $script:LogFile }
        Add-Content -Path $script:LogFile -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $Message) -Encoding UTF8
    } catch { }
}

function Get-Settings {
    $s = [ordered]@{ PauseOnRemove = $true; AutoDefaultDevice = $true; ShowOnConnect = $true; LowBatteryPercent = 20 }
    if (Test-Path $script:SettingsFile) {
        try {
            $saved = Get-Content $script:SettingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($k in @($s.Keys)) { if ($null -ne $saved.$k) { $s[$k] = $saved.$k } }
        } catch { }
    }
    return $s
}

function Save-Settings {
    New-Item -ItemType Directory -Force -Path (Split-Path $script:SettingsFile) | Out-Null
    $script:Settings | ConvertTo-Json | Set-Content -Path $script:SettingsFile -Encoding UTF8
}

$script:Settings = Get-Settings

trap { Write-Log "error: $_ $($_.ScriptStackTrace)"; continue }
Write-Log "PodPeek script en Windows $([Environment]::OSVersion.Version), PowerShell $($PSVersionTable.PSVersion)"

# ─── Partes nativas (audio y ventana) ───────────────────────────────────────
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $script:Here 'PodPeekNative.cs')))

# ─── WinRT: Bluetooth y control de medios ───────────────────────────────────
$null = [Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
$null = [Windows.Devices.Bluetooth.Advertisement.BluetoothLEManufacturerData, Windows.Devices.Bluetooth, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType = WindowsRuntime]
$null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]

# Las operaciones asíncronas de WinRT, esperadas desde PowerShell 5.1
$script:AsTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
} | Select-Object -First 1

function Wait-WinRT($Operation, [Type]$ResultType) {
    $task = $script:AsTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $null = $task.Wait(5000)
    return $task.Result
}

# ─── El anuncio Bluetooth de los AirPods ────────────────────────────────────
# Datos de fabricante de Apple (0x004C), mensaje 0x07 "proximity pairing": batería, carga y si están puestos.
function ConvertFrom-Proximity([byte[]]$d) {
    if ($d.Length -lt 9 -or $d[0] -ne 0x07) { return $null }
    $status = $d[5]
    $flip = ($status -band 0x20) -eq 0
    $hi = $d[6] -shr 4; $lo = $d[6] -band 0x0F
    $charge = $d[7] -shr 4; $ear = $status -band 0x0F
    $pct = { param($raw) if ($raw -le 10) { $raw * 10 } else { $null } }
    [pscustomobject]@{
        Model         = ([int]$d[3] -shl 8) -bor $d[4]
        Left          = & $pct ($(if ($flip) { $hi } else { $lo }))
        Right         = & $pct ($(if ($flip) { $lo } else { $hi }))
        Case          = & $pct ($d[7] -band 0x0F)
        LeftCharging  = ($charge -band $(if ($flip) { 0x02 } else { 0x01 })) -ne 0
        RightCharging = ($charge -band $(if ($flip) { 0x01 } else { 0x02 })) -ne 0
        CaseCharging  = ($charge -band 0x04) -ne 0
        LeftInEar     = ($ear -band $(if ($flip) { 0x08 } else { 0x02 })) -ne 0
        RightInEar    = ($ear -band $(if ($flip) { 0x02 } else { 0x08 })) -ne 0
    }
}

function Get-ModelName([int]$m) {
    switch ($m) {
        0x0220 { 'AirPods' } 0x0F20 { 'AirPods (2.ª gen.)' } 0x1320 { 'AirPods (3.ª gen.)' }
        0x1920 { 'AirPods 4' } 0x1B20 { 'AirPods 4' } 0x0E20 { 'AirPods Pro' }
        0x1420 { 'AirPods Pro 2' } 0x2420 { 'AirPods Pro 2' } 0x0A20 { 'AirPods Max' }
        default { 'AirPods' }
    }
}

# ─── Colores y ventana (estilo macOS) ───────────────────────────────────────
function New-Brush([string]$hex) {
    $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex))
    $b.Freeze(); return $b
}

function Get-LevelHex([int]$p) { if ($p -gt 20) { '#34C759' } elseif ($p -gt 10) { '#FF9F0A' } else { '#FF3B30' } }

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="PodPeek" Width="356" SizeToContent="Height" ResizeMode="NoResize" ShowInTaskbar="False"
        ShowActivated="False" Topmost="True" Background="Transparent" UseLayoutRounding="True"
        FontFamily="Segoe UI Variable Text, Segoe UI" TextOptions.TextFormattingMode="Ideal">
  <WindowChrome.WindowChrome>
    <WindowChrome GlassFrameThickness="-1" CaptionHeight="0" ResizeBorderThickness="0" CornerRadius="0" UseAeroCaptionButtons="False"/>
  </WindowChrome.WindowChrome>
  <Window.Resources>
    <Style x:Key="Pill" TargetType="Button">
      <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
      <Setter Property="Background" Value="{DynamicResource ButtonBrush}"/>
      <Setter Property="FontSize" Value="13"/><Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Padding" Value="18,7"/><Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template"><Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="B" CornerRadius="15" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="B" Property="Background" Value="{DynamicResource ButtonHoverBrush}"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="B" Property="Opacity" Value="0.7"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="B" Property="Opacity" Value="0.5"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value></Setter>
    </Style>
    <Style x:Key="Track" TargetType="Ellipse">
      <Setter Property="StrokeThickness" Value="4.5"/><Setter Property="Stroke" Value="{DynamicResource TrackBrush}"/>
    </Style>
    <Style x:Key="Arc" TargetType="Ellipse">
      <Setter Property="StrokeThickness" Value="4.5"/><Setter Property="StrokeDashCap" Value="Round"/>
      <Setter Property="RenderTransformOrigin" Value="0.5,0.5"/>
      <Setter Property="RenderTransform"><Setter.Value><RotateTransform Angle="-90"/></Setter.Value></Setter>
    </Style>
    <Style x:Key="Icon" TargetType="Path">
      <Setter Property="Fill" Value="{DynamicResource IconBrush}"/><Setter Property="Stretch" Value="Uniform"/>
      <Setter Property="Width" Value="26"/><Setter Property="Height" Value="26"/>
    </Style>
    <Style x:Key="Label" TargetType="TextBlock">
      <Setter Property="FontSize" Value="12"/><Setter Property="HorizontalAlignment" Value="Center"/>
      <Setter Property="Foreground" Value="{DynamicResource SecondaryTextBrush}"/>
    </Style>
    <Style x:Key="Value" TargetType="TextBlock">
      <Setter Property="FontSize" Value="15"/><Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/>
    </Style>
    <Style x:Key="Bolt" TargetType="TextBlock">
      <Setter Property="Text" Value="&#xE945;"/><Setter Property="FontSize" Value="11"/>
      <Setter Property="FontFamily" Value="Segoe Fluent Icons, Segoe MDL2 Assets"/>
      <Setter Property="Margin" Value="3,1,0,0"/><Setter Property="VerticalAlignment" Value="Center"/>
      <Setter Property="Foreground" Value="{DynamicResource TextBrush}"/><Setter Property="Visibility" Value="Collapsed"/>
    </Style>
    <GeometryGroup x:Key="Pod" FillRule="Nonzero">
      <EllipseGeometry Center="11,9" RadiusX="7.5" RadiusY="7.5"/>
      <RectangleGeometry Rect="13.5,9,5.5,18" RadiusX="2.75" RadiusY="2.75"/>
    </GeometryGroup>
    <GeometryGroup x:Key="CaseGeo" FillRule="EvenOdd">
      <RectangleGeometry Rect="2,5,24,19" RadiusX="7" RadiusY="7"/>
      <RectangleGeometry Rect="2,11,24,1.2"/>
      <EllipseGeometry Center="14,17.5" RadiusX="1.1" RadiusY="1.1"/>
    </GeometryGroup>
  </Window.Resources>

  <Border x:Name="Root" Background="{DynamicResource PanelBrush}" Padding="22,18,22,20">
    <Border.RenderTransform><TranslateTransform/></Border.RenderTransform>
    <StackPanel>
      <Grid Margin="0,0,0,18">
        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
        <TextBlock x:Name="TitleText" Text="AirPods" FontSize="16" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"
                   FontFamily="Segoe UI Variable Display, Segoe UI" Foreground="{DynamicResource TextBrush}"/>
        <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
          <Ellipse x:Name="StatusDot" Width="7" Height="7" Margin="0,1,6,0" Fill="#8E8E93"/>
          <TextBlock x:Name="StatusText" Text="Sin conectar" FontSize="12" Foreground="{DynamicResource SecondaryTextBrush}"/>
        </StackPanel>
      </Grid>

      <UniformGrid Columns="3" HorizontalAlignment="Center">
        <StackPanel Width="92">
          <Grid Width="64" Height="64" Margin="0,0,0,10">
            <Ellipse Style="{StaticResource Track}"/><Ellipse x:Name="ArcL" Style="{StaticResource Arc}"/>
            <Path x:Name="IconL" Style="{StaticResource Icon}" Data="{StaticResource Pod}"/>
          </Grid>
          <TextBlock Text="Izquierdo" Style="{StaticResource Label}"/>
          <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,2,0,0">
            <TextBlock x:Name="ValueL" Text="—" Style="{StaticResource Value}"/><TextBlock x:Name="BoltL" Style="{StaticResource Bolt}"/>
          </StackPanel>
        </StackPanel>
        <StackPanel Width="92">
          <Grid Width="64" Height="64" Margin="0,0,0,10">
            <Ellipse Style="{StaticResource Track}"/><Ellipse x:Name="ArcR" Style="{StaticResource Arc}"/>
            <Path x:Name="IconR" Style="{StaticResource Icon}" Data="{StaticResource Pod}" RenderTransformOrigin="0.5,0.5">
              <Path.RenderTransform><ScaleTransform ScaleX="-1"/></Path.RenderTransform>
            </Path>
          </Grid>
          <TextBlock Text="Derecho" Style="{StaticResource Label}"/>
          <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,2,0,0">
            <TextBlock x:Name="ValueR" Text="—" Style="{StaticResource Value}"/><TextBlock x:Name="BoltR" Style="{StaticResource Bolt}"/>
          </StackPanel>
        </StackPanel>
        <StackPanel Width="92">
          <Grid Width="64" Height="64" Margin="0,0,0,10">
            <Ellipse Style="{StaticResource Track}"/><Ellipse x:Name="ArcC" Style="{StaticResource Arc}"/>
            <Path x:Name="IconC" Style="{StaticResource Icon}" Data="{StaticResource CaseGeo}"/>
          </Grid>
          <TextBlock Text="Estuche" Style="{StaticResource Label}"/>
          <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,2,0,0">
            <TextBlock x:Name="ValueC" Text="—" Style="{StaticResource Value}"/><TextBlock x:Name="BoltC" Style="{StaticResource Bolt}"/>
          </StackPanel>
        </StackPanel>
      </UniformGrid>

      <TextBlock x:Name="MessageText" Margin="0,14,0,0" FontSize="12" TextWrapping="Wrap" TextAlignment="Center"
                 Visibility="Collapsed" Foreground="{DynamicResource SecondaryTextBrush}"/>
      <Button x:Name="ConnectButton" Content="Conectar" Style="{StaticResource Pill}" Margin="0,16,0,0" HorizontalAlignment="Center"/>
    </StackPanel>
  </Border>
</Window>
'@

$script:Window = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
$script:UI = @{}
foreach ($n in 'Root','TitleText','StatusDot','StatusText','ArcL','ArcR','ArcC','IconL','IconR','IconC',
               'ValueL','ValueR','ValueC','BoltL','BoltR','BoltC','MessageText','ConnectButton') {
    $script:UI[$n] = $script:Window.FindName($n)
}
$script:UI.Slide = $script:UI.Root.RenderTransform

function Set-Theme {
    $dark = [PodPeekNative.Win]::AppsDark
    $r = $script:Window.Resources
    $r['TextBrush']          = New-Brush $(if ($dark) { '#F5F5F7' } else { '#1D1D1F' })
    $r['SecondaryTextBrush'] = New-Brush $(if ($dark) { '#98989D' } else { '#6E6E73' })
    $r['TrackBrush']         = New-Brush $(if ($dark) { '#2EFFFFFF' } else { '#17000000' })
    $r['IconBrush']          = New-Brush $(if ($dark) { '#E5E5EA' } else { '#3A3A3C' })
    $r['ButtonBrush']        = New-Brush $(if ($dark) { '#26FFFFFF' } else { '#14000000' })
    $r['ButtonHoverBrush']   = New-Brush $(if ($dark) { '#3DFFFFFF' } else { '#24000000' })
    $r['PanelBrush'] = if ([PodPeekNative.Win]::SupportsBackdrop) {
        New-Brush $(if ($dark) { '#66161618' } else { '#99F5F5F7' })
    } else {
        New-Brush $(if ($dark) { '#2C2C2E' } else { '#F2F2F7' })
    }
    $h = (New-Object System.Windows.Interop.WindowInteropHelper $script:Window).Handle
    if ($h -ne [IntPtr]::Zero) { [PodPeekNative.Win]::Apply($h, $dark) }
}

$script:Window.Add_SourceInitialized({
    $h = (New-Object System.Windows.Interop.WindowInteropHelper $script:Window).Handle
    $src = [System.Windows.Interop.HwndSource]::FromHwnd($h)
    if ($src) { $src.CompositionTarget.BackgroundColor = [System.Windows.Media.Colors]::Transparent }
    [PodPeekNative.Win]::MakeToolWindow($h)
    Set-Theme
})

# Anillo = elipse con un trazo discontinuo: animando el desplazamiento del trazo se "rellena" como en macOS
$script:RingLength = [Math]::PI * (64 - 4.5) / 4.5
$script:Shown = @{ L = $null; R = $null; C = $null }

function Set-Ring([string]$k, $percent, [bool]$charging, [bool]$animate) {
    $arc = $script:UI["Arc$k"]
    $script:UI["Value$k"].Text = if ($null -ne $percent) { "$percent %" } else { '—' }
    $script:UI["Bolt$k"].Visibility = if ($charging -and $null -ne $percent) { 'Visible' } else { 'Collapsed' }
    $script:UI["Icon$k"].Opacity = if ($null -ne $percent) { 1 } else { 0.35 }
    $arc.Stroke = New-Brush (Get-LevelHex $(if ($null -ne $percent) { $percent } else { 100 }))
    $arc.StrokeDashArray = New-Object System.Windows.Media.DoubleCollection (,[double[]]@($script:RingLength, $script:RingLength))
    $arc.Visibility = if ($percent -gt 0) { 'Visible' } else { 'Hidden' }
    $target = $script:RingLength * (1 - [double]$(if ($null -ne $percent) { $percent } else { 0 }) / 100)
    if ($animate) {
        $from = if ($script:Shown[$k] -eq '__replay__') { $script:RingLength } else { $arc.StrokeDashOffset }
        $anim = New-Object System.Windows.Media.Animation.DoubleAnimation ($from, $target, [TimeSpan]::FromMilliseconds(700))
        $ease = New-Object System.Windows.Media.Animation.CubicEase; $ease.EasingMode = 'EaseOut'; $anim.EasingFunction = $ease
        $arc.BeginAnimation([System.Windows.Shapes.Shape]::StrokeDashOffsetProperty, $anim)
    } else {
        $arc.BeginAnimation([System.Windows.Shapes.Shape]::StrokeDashOffsetProperty, $null)
        $arc.StrokeDashOffset = $target
    }
    $script:Shown[$k] = $percent
}

function Set-Message([string]$text) {
    $script:UI.MessageText.Text = $text
    $script:UI.MessageText.Visibility = if ($text) { 'Visible' } else { 'Collapsed' }
}

function Update-Popup([bool]$animate) {
    $s = $script:Last
    $name = $script:DeviceName
    $script:UI.TitleText.Text = if ($name) { $name } elseif ($s) { Get-ModelName $s.Model } else { 'AirPods' }
    $script:UI.StatusText.Text = if ($script:Connected) { 'Conectados' } elseif ($s) { 'Cerca, sin conectar' } else { 'Sin conectar' }
    $script:UI.StatusDot.Fill = New-Brush $(if ($script:Connected) { '#34C759' } else { '#8E8E93' })
    $script:UI.ConnectButton.Visibility = if ($script:Connected) { 'Collapsed' } else { 'Visible' }
    if ($script:Connected) { Set-Message $null }
    Set-Ring 'L' $(if ($s) { $s.Left }) ([bool]($s -and $s.LeftCharging)) $animate
    Set-Ring 'R' $(if ($s) { $s.Right }) ([bool]($s -and $s.RightCharging)) $animate
    Set-Ring 'C' $(if ($s) { $s.Case }) ([bool]($s -and $s.CaseCharging)) $animate
}

$script:Showing = $false
$script:HideTimer = New-Object System.Windows.Threading.DispatcherTimer
$script:HideTimer.Add_Tick({ Hide-Popup })
$script:Window.Add_MouseEnter({ $script:HideTimer.Stop() })
$script:Window.Add_MouseLeave({ if ($script:Showing) { $script:HideTimer.Start() } })

function Show-Popup([double]$seconds) {
    $script:HideTimer.Stop()
    $script:HideTimer.Interval = [TimeSpan]::FromSeconds($seconds)
    if (-not $script:Showing) {
        $script:Showing = $true
        $script:UI.Root.Opacity = 0
        $script:Window.Show()
        $script:Window.UpdateLayout()
        $area = [System.Windows.SystemParameters]::WorkArea
        $script:Window.Left = $area.Right - $script:Window.ActualWidth - 12
        $script:Window.Top = $area.Bottom - $script:Window.ActualHeight - 12
        $ease = New-Object System.Windows.Media.Animation.CubicEase; $ease.EasingMode = 'EaseOut'
        $fade = New-Object System.Windows.Media.Animation.DoubleAnimation (0, 1, [TimeSpan]::FromMilliseconds(220)); $fade.EasingFunction = $ease
        $slide = New-Object System.Windows.Media.Animation.DoubleAnimation (18, 0, [TimeSpan]::FromMilliseconds(320)); $slide.EasingFunction = $ease
        $script:UI.Root.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $fade)
        $script:UI.Slide.BeginAnimation([System.Windows.Media.TranslateTransform]::YProperty, $slide)
        foreach ($k in 'L','R','C') { $script:Shown[$k] = '__replay__' }
        Update-Popup $true
    }
    if (-not $script:Window.IsMouseOver) { $script:HideTimer.Start() }
}

function Hide-Popup {
    $script:HideTimer.Stop()
    if (-not $script:Showing) { return }
    $script:Showing = $false
    $fade = New-Object System.Windows.Media.Animation.DoubleAnimation (0, [TimeSpan]::FromMilliseconds(160))
    $fade.Add_Completed({ if (-not $script:Showing) { $script:Window.Hide() } })
    $script:UI.Root.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $fade)
}

# ─── Icono de la bandeja ────────────────────────────────────────────────────
function New-TrayIcon {
    $bmp = New-Object System.Drawing.Bitmap 32, 32
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $color = if ([PodPeekNative.Win]::TaskbarDark) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(0x1D, 0x1D, 0x1F) }
    $brush = New-Object System.Drawing.SolidBrush $color
    foreach ($pod in @(@{ X = 2; Head = 2; Stem = 8 }, @{ X = 17; Head = 20; Stem = 20 })) {
        $g.FillEllipse($brush, $pod.Head, 3, 11, 11)
        $g.FillRectangle($brush, $pod.Stem, 9, 5, 18)
        $g.FillEllipse($brush, $pod.Stem, 24, 5, 5)
    }
    $g.Dispose()
    $icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
    return $icon
}

$script:Tray = New-Object System.Windows.Forms.NotifyIcon
$script:Tray.Icon = New-TrayIcon
$script:Tray.Text = 'PodPeek'
$script:Tray.Visible = $true
$script:Tray.Add_MouseClick({ param($s, $e)
    if ($e.Button -eq 'Left') { if ($script:Showing) { Hide-Popup } else { Show-Popup 8 } }
})

function Update-Tray {
    $s = $script:Last
    $fmt = { param($v) if ($null -ne $v) { "$v %" } else { '—' } }
    $text = if ($s) { "I $(& $fmt $s.Left) · D $(& $fmt $s.Right) · Estuche $(& $fmt $s.Case)" }
            elseif ($script:Connected) { 'AirPods conectados' } else { 'PodPeek · AirPods no detectados' }
    $script:Tray.Text = if ($text.Length -gt 63) { $text.Substring(0, 63) } else { $text }
}

function New-Toggle([string]$text, [string]$key) {
    $item = New-Object System.Windows.Forms.ToolStripMenuItem $text
    $item.CheckOnClick = $true
    $item.Checked = [bool]$script:Settings[$key]
    $item.Tag = $key
    $item.Add_CheckedChanged({ param($s) $script:Settings[$s.Tag] = $s.Checked; Save-Settings })
    return $item
}

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$menu.ShowImageMargin = $false
$menu.Font = New-Object System.Drawing.Font('Segoe UI', 9.5)
$null = $menu.Items.Add('Mostrar batería', $null, { Show-Popup 8 })
$null = $menu.Items.Add('Conectar AirPods', $null, { Connect-AirPods })
$null = $menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$null = $menu.Items.Add((New-Toggle 'Pausar la música al quitármelos' 'PauseOnRemove'))
$null = $menu.Items.Add((New-Toggle 'Usarlos para todo al conectarlos (también Teams)' 'AutoDefaultDevice'))
$null = $menu.Items.Add((New-Toggle 'Mostrar la batería al conectarlos' 'ShowOnConnect'))
$startup = New-Object System.Windows.Forms.ToolStripMenuItem 'Iniciar con Windows'
$startup.CheckOnClick = $true
$startup.Checked = Test-Path $script:StartupVbs
$startup.Add_CheckedChanged({ param($s)
    if ($s.Checked) {
        $ps1 = Join-Path $script:Here 'podpeek.ps1'
        $vbs = "Set sh = CreateObject(""WScript.Shell"")`r`nsh.Run ""powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """"$ps1"""" -Background"", 0, False"
        Set-Content -Path $script:StartupVbs -Value $vbs -Encoding ASCII
    } elseif (Test-Path $script:StartupVbs) { Remove-Item $script:StartupVbs }
})
$null = $menu.Items.Add($startup)
$null = $menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
$null = $menu.Items.Add('Abrir registro', $null, { Start-Process $script:LogDir })
$null = $menu.Items.Add('Salir', $null, { Stop-PodPeek })
$script:Tray.ContextMenuStrip = $menu

# ─── Lógica ─────────────────────────────────────────────────────────────────
$script:Last = $null
$script:LastSeen = [DateTime]::MinValue
$script:Address = 0
$script:Rssi = -999
$script:ShownSinceSeen = $false
$script:WasBothIn = $false
$script:PausedByUs = $false
$script:WarnedAt = $null
$script:Connected = [PodPeekNative.Audio]::IsConnected()
$script:DeviceName = [PodPeekNative.Audio]::DeviceName()
$script:LastRawLog = [DateTime]::MinValue

function Get-MediaSession {
    try {
        $mgr = Wait-WinRT ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
        return $mgr.GetCurrentSession()
    } catch { Write-Log "control de medios no disponible: $_"; return $null }
}

function On-Status($s) {
    $script:Last = $s
    Update-Tray
    if ($script:Showing) { Update-Popup $true }

    if (-not $script:ShownSinceSeen -and $script:Settings.ShowOnConnect) {
        $script:ShownSinceSeen = $true
        Show-Popup 6
    }

    $low = @($s.Left, $s.Right) | Where-Object { $null -ne $_ } | Measure-Object -Minimum
    if ($low.Count -gt 0) {
        if ($low.Minimum -le $script:Settings.LowBatteryPercent -and $null -eq $script:WarnedAt) {
            $script:WarnedAt = $low.Minimum
            Set-Message "Batería baja: $($low.Minimum) %"
            Show-Popup 8
        } elseif ($low.Minimum -gt $script:Settings.LowBatteryPercent + 10) { $script:WarnedAt = $null }
    }

    $bothIn = $s.LeftInEar -and $s.RightInEar
    if ($script:Settings.PauseOnRemove -and $script:Connected) {
        if ($script:WasBothIn -and -not $bothIn) {
            $session = Get-MediaSession
            if ($session -and $session.GetPlaybackInfo().PlaybackStatus -eq 'Playing') {
                $script:PausedByUs = Wait-WinRT ($session.TryPauseAsync()) ([bool])
                Write-Log "pausa al quitar un auricular: $script:PausedByUs"
            }
        } elseif (-not $script:WasBothIn -and $bothIn -and $script:PausedByUs) {
            $script:PausedByUs = $false
            $session = Get-MediaSession
            if ($session) { $ok = Wait-WinRT ($session.TryPlayAsync()) ([bool]); Write-Log "reanudar: $ok" }
        }
    }
    $script:WasBothIn = $bothIn
}

function On-ConnectionChanged([bool]$connected) {
    $script:Connected = $connected
    $script:DeviceName = [PodPeekNative.Audio]::DeviceName()
    Write-Log $(if ($connected) { 'AirPods conectados' } else { 'AirPods desconectados' })
    if ($connected -and $script:Settings.AutoDefaultDevice) {
        try { Write-Log ("predeterminados: " + [PodPeekNative.Audio]::MakeDefault()) } catch { Write-Log "predeterminados: $_" }
    }
    $script:UI.ConnectButton.IsEnabled = $true
    $script:UI.ConnectButton.Content = 'Conectar'
    Update-Tray
    if ($script:Showing) { Update-Popup $true }
    if ($connected -and $script:Settings.ShowOnConnect) { Show-Popup 5 }
}

$script:ConnectDeadline = $null
function Connect-AirPods {
    if ($script:Connected) { Show-Popup 4; return }
    $script:UI.ConnectButton.IsEnabled = $false
    $script:UI.ConnectButton.Content = 'Conectando…'
    Set-Message $null
    Show-Popup 12
    $result = [PodPeekNative.Audio]::Connect()
    Write-Log "conectar: $result"
    if ($result -notmatch '0x0000000') {
        $script:UI.ConnectButton.IsEnabled = $true
        $script:UI.ConnectButton.Content = 'Conectar'
        Set-Message 'No se han podido conectar. Saca los AirPods del estuche y vuelve a probar.'
    } else {
        $script:ConnectDeadline = (Get-Date).AddSeconds(10)
    }
}
$script:UI.ConnectButton.Add_Click({ Connect-AirPods })

# ─── Bluetooth: solo anuncios de Apple, y con señal suficiente ──────────────
$script:Watcher = New-Object Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher
$script:Watcher.ScanningMode = 'Passive'
$appleFilter = New-Object Windows.Devices.Bluetooth.Advertisement.BluetoothLEManufacturerData
$appleFilter.CompanyId = 0x004C
$script:Watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add($appleFilter)
$script:Watcher.SignalStrengthFilter.InRangeThresholdInDBm = -72
$null = Register-ObjectEvent -InputObject $script:Watcher -EventName Received -SourceIdentifier PodPeekAdv
try { $script:Watcher.Start(); Write-Log 'escáner Bluetooth en marcha' } catch { Write-Log "no se pudo arrancar el escáner Bluetooth: $_" }

function Read-Advertisements {
    $events = @(Get-Event -SourceIdentifier PodPeekAdv -ErrorAction SilentlyContinue)
    foreach ($ev in $events) { Remove-Event -EventIdentifier $ev.EventIdentifier }
    # Nos quedamos con los últimos de cada ronda: llegan muchos y basta con el más reciente
    $latest = $null
    foreach ($ev in $events) {
        $a = $ev.SourceEventArgs
        if ($a.RawSignalStrengthInDBm -lt -72) { continue }
        foreach ($m in $a.Advertisement.ManufacturerData) {
            if ($m.CompanyId -ne 0x004C) { continue }
            $bytes = New-Object byte[] $m.Data.Length
            [Windows.Storage.Streams.DataReader]::FromBuffer($m.Data).ReadBytes($bytes)
            $s = ConvertFrom-Proximity $bytes
            if (-not $s) { continue }
            $now = Get-Date
            $same = $a.BluetoothAddress -eq $script:Address
            $stale = ($now - $script:LastSeen).TotalSeconds -gt 8
            # Varios AirPods cerca (oficina): seguimos a los de más señal y solo cambiamos si otros llegan claramente más fuertes
            if (-not $same -and -not $stale -and $a.RawSignalStrengthInDBm -lt $script:Rssi + 6) { continue }
            $script:Address = $a.BluetoothAddress
            $script:Rssi = $a.RawSignalStrengthInDBm
            $script:LastSeen = $now
            $latest = $s
            if (($now - $script:LastRawLog).TotalMinutes -ge 1) {
                $script:LastRawLog = $now
                Write-Log ("anuncio {0} rssi={1}" -f ([BitConverter]::ToString($bytes)), $a.RawSignalStrengthInDBm)
            }
        }
    }
    if ($latest) { On-Status $latest }
}

# ─── Bucle: cada 250 ms anuncios; cada 2 s conexión ─────────────────────────
$script:Ticks = 0
$script:Timer = New-Object System.Windows.Threading.DispatcherTimer
$script:Timer.Interval = [TimeSpan]::FromMilliseconds(250)
$script:Timer.Add_Tick({
    try {
        Read-Advertisements
        $script:Ticks++
        if ($script:Ticks % 8 -eq 0) {
            $now = [PodPeekNative.Audio]::IsConnected()
            if ($now -ne $script:Connected) { On-ConnectionChanged $now }
            if ($script:ConnectDeadline -and (Get-Date) -gt $script:ConnectDeadline) {
                $script:ConnectDeadline = $null
                if (-not $script:Connected) {
                    $script:UI.ConnectButton.IsEnabled = $true
                    $script:UI.ConnectButton.Content = 'Conectar'
                    Set-Message 'Windows no ha conseguido conectarlos. ¿Están cerca y fuera del estuche?'
                }
            }
            if ($script:Last -and ((Get-Date) - $script:LastSeen).TotalSeconds -gt 30) {
                $script:Last = $null; $script:ShownSinceSeen = $false; $script:WasBothIn = $false
                $script:Address = 0; $script:Rssi = -999
                Update-Tray
                if ($script:Showing) { Update-Popup $false }
            }
        }
    } catch { Write-Log "error en el bucle: $_" }
})
$script:Timer.Start()

# Cambio de tema claro/oscuro de Windows
$script:ThemeDark = [PodPeekNative.Win]::AppsDark
$script:ThemeTimer = New-Object System.Windows.Threading.DispatcherTimer
$script:ThemeTimer.Interval = [TimeSpan]::FromSeconds(5)
$script:ThemeTimer.Add_Tick({
    if ([PodPeekNative.Win]::AppsDark -ne $script:ThemeDark) {
        $script:ThemeDark = [PodPeekNative.Win]::AppsDark
        Set-Theme
        $old = $script:Tray.Icon; $script:Tray.Icon = New-TrayIcon; $old.Dispose()
    }
})
$script:ThemeTimer.Start()

function Stop-PodPeek {
    Write-Log 'salir'
    try { $script:Watcher.Stop() } catch { }
    Unregister-Event -SourceIdentifier PodPeekAdv -ErrorAction SilentlyContinue
    $script:Tray.Visible = $false
    $script:Tray.Dispose()
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.InvokeShutdown()
}

Set-Theme
Update-Tray
Update-Popup $false
if (-not $Background) { Show-Popup 6 }
[System.Windows.Threading.Dispatcher]::Run()
$script:mutex.ReleaseMutex()
