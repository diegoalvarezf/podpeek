# Compila las partes en C# de PodPeek. Add-Type no admite referencias a .winmd (WinRT), así que el escáner
# Bluetooth se compila con el compilador de C# de .NET Framework, que sí las acepta. Lo usan podpeek.ps1 y el CI.

function Add-CompiledType([string]$Path, [string[]]$References = @()) {
    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    $params = New-Object System.CodeDom.Compiler.CompilerParameters
    $params.GenerateInMemory = $true
    $params.ReferencedAssemblies.AddRange([string[]](@('System.dll', 'System.Core.dll') + $References))
    $result = $provider.CompileAssemblyFromSource($params, [IO.File]::ReadAllText($Path))
    if ($result.Errors.HasErrors) {
        throw ("No compila {0}:`n{1}" -f (Split-Path $Path -Leaf), (($result.Errors | ForEach-Object { $_.ToString() }) -join "`n"))
    }
}

function Import-PodPeekNative([string]$Dir) {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    Add-CompiledType (Join-Path $Dir 'PodPeekNative.cs')
    $winmd = Join-Path $env:WINDIR 'System32\WinMetadata'
    $framework = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
    Add-CompiledType (Join-Path $Dir 'PodPeekBle.cs') @(
        (Join-Path $winmd 'Windows.Devices.winmd'),
        (Join-Path $winmd 'Windows.Foundation.winmd'),
        (Join-Path $winmd 'Windows.Storage.winmd'),
        (Join-Path $framework 'System.Runtime.dll')
    )
}
