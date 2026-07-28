param(
    [string]$Root = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

function Add-Failure {
    param([System.Collections.Generic.List[string]]$List, [string]$Message)
    $List.Add($Message)
}

$failures = [System.Collections.Generic.List[string]]::new()
$rootPath = (Resolve-Path $Root).Path

Write-Host "Validazione statica Nexora Mix Studio V8 Tablet Console..." -ForegroundColor Cyan

# 1. XML/XAML ben formato.
$xmlFiles = Get-ChildItem $rootPath -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
    $_.Extension -in '.xaml', '.csproj', '.slnx', '.props', '.targets'
}

foreach ($file in $xmlFiles) {
    if ($file.Extension -eq '.slnx') { continue }
    try {
        [xml](Get-Content $file.FullName -Raw) | Out-Null
    }
    catch {
        Add-Failure $failures "XML/XAML non valido: $($file.FullName) - $($_.Exception.Message)"
    }
}

# 2. Regressione nota: RangeBase.Value non deve scrivere su MeterPeak.
$xamlFiles = Get-ChildItem $rootPath -Recurse -Filter *.xaml -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}
foreach ($file in $xamlFiles) {
    $content = Get-Content $file.FullName -Raw

    $unsafeMeterBindings = [regex]::Matches(
        $content,
        '<ProgressBar\b[^>]*\bValue="\{Binding\s+MeterPeak(?![^}]*Mode=OneWay)[^}]*\}"',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    if ($unsafeMeterBindings.Count -gt 0) {
        Add-Failure $failures "Binding MeterPeak non OneWay: $($file.FullName)"
    }

    if ($content -match 'Value="\{Binding\s+MeterPeak\}"') {
        Add-Failure $failures "Binding MeterPeak implicito rilevato: $($file.FullName)"
    }
}

# 3. La libreria non deve tentare di modificare proprietà descrittive calcolate.
$mainWindow = Join-Path $rootPath 'NexoraMix.App\Views\MainWindow.xaml'
if (Test-Path $mainWindow) {
    $mainXaml = Get-Content $mainWindow -Raw
    if ($mainXaml -notmatch '<DataGrid\b[^>]*\bIsReadOnly="True"') {
        Add-Failure $failures 'La DataGrid della libreria non è esplicitamente IsReadOnly=True.'
    }

    foreach ($property in @('BpmText','BeatCountText','BarCountText','DurationText','CueText','SourceDisplay')) {
        $pattern = 'Binding="\{Binding\s+' + [regex]::Escape($property) + '(?![^}]*Mode=OneWay)[^}]*\}"'
        if ($mainXaml -match $pattern) {
            Add-Failure $failures "Colonna DataGrid non OneWay: $property"
        }
    }
}

# 4. Errori C# evidenti che non devono entrare nella build.
$csFiles = Get-ChildItem $rootPath -Recurse -Filter *.cs -File | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}
foreach ($file in $csFiles) {
    $content = Get-Content $file.FullName -Raw

    if ($content -match 'NotImplementedException\s*\(') {
        Add-Failure $failures "NotImplementedException presente: $($file.FullName)"
    }

    if ($content -match 'catch\s*(\([^)]*\))?\s*\{\s*\}') {
        Add-Failure $failures "catch vuoto presente: $($file.FullName)"
    }

    $usesIoTypes = $content -match '\b(FileStream|FileMode|FileAccess|FileShare|FileOptions|FileInfo|IOException|FileNotFoundException|StreamWriter)\b'
    $hasIoNamespace = $content -match '(?m)^using\s+System\.IO\s*;' -or $content -match '\bSystem\.IO\.'
    if ($usesIoTypes -and -not $hasIoNamespace) {
        Add-Failure $failures "Tipi System.IO senza namespace esplicito: $($file.FullName)"
    }
}


# 4b. Requisiti BeatVision: conteggio battute, TAP BPM e layout scorrevole.
$deckViewModel = Join-Path $rootPath 'NexoraMix.App\ViewModels\DeckViewModel.cs'
$analyzer = Join-Path $rootPath 'NexoraMix.Audio\Analysis\BpmAnalyzer.cs'
$promptWindow = Join-Path $rootPath 'NexoraMix.App\Views\TextPromptWindow.xaml'

if (Test-Path $deckViewModel) {
    $deckContent = Get-Content $deckViewModel -Raw
    foreach ($requiredToken in @('BeatCounterText','BeatProgressText','TapBpmCommand','RegisterTapBpm')) {
        if ($deckContent -notmatch [regex]::Escape($requiredToken)) {
            Add-Failure $failures "Funzione BeatVision mancante in DeckViewModel: $requiredToken"
        }
    }
}

if (Test-Path $analyzer) {
    $analyzerContent = Get-Content $analyzer -Raw
    foreach ($requiredToken in @('DetectedBeatCount','EstimatedBarCount','TrackBeatPositions')) {
        if ($analyzerContent -notmatch [regex]::Escape($requiredToken)) {
            Add-Failure $failures "Funzione BeatVision mancante nell'analizzatore: $requiredToken"
        }
    }
}

if (Test-Path $mainWindow) {
    $mainXaml = Get-Content $mainWindow -Raw
    if ($mainXaml -notmatch 'VerticalScrollBarVisibility="Auto"') {
        Add-Failure $failures 'MainWindow priva di area scorrevole verticale per DPI elevato.'
    }
}

if (Test-Path $promptWindow) {
    $promptXaml = Get-Content $promptWindow -Raw
    if ($promptXaml -notmatch '<RowDefinition Height="Auto"') {
        Add-Failure $failures 'TextPromptWindow non contiene un footer ad altezza automatica.'
    }
}

# 5. I file principali devono esistere.
foreach ($required in @(
    'NexoraMixStudio.sln',
    'NexoraMix.App\NexoraMix.App.csproj',
    'NexoraMix.Core\NexoraMix.Core.csproj',
    'NexoraMix.Audio\NexoraMix.Audio.csproj',
    'NexoraMix.App\Views\MainWindow.xaml',
    'NexoraMix.App\ViewModels\DeckViewModel.cs',
    'NexoraMix.App\ViewModels\MainViewModel.V7.cs',
    'NexoraMix.ControllerTests\NexoraMix.ControllerTests.csproj',
    'NexoraMix.App\Remote\TabletRemoteHost.cs',
    'NexoraMix.App\RemoteUi\index.html',
    'NexoraMix.App\RemoteUi\styles.css',
    'NexoraMix.App\RemoteUi\app.js',
    'NexoraMix.Tablet.Android\NexoraMix.Tablet.Android.csproj'
)) {
    if (-not (Test-Path (Join-Path $rootPath $required))) {
        Add-Failure $failures "File obbligatorio mancante: $required"
    }
}


# 6. Requisiti V7: quattro deck, Auto Mashup e Generic MIDI.
$masterEngine = Join-Path $rootPath 'NexoraMix.Audio\Playback\MasterAudioEngine.cs'
$v7ViewModel = Join-Path $rootPath 'NexoraMix.App\ViewModels\MainViewModel.V7.cs'
$midiService = Join-Path $rootPath 'NexoraMix.Audio\Controllers\GenericMidiControllerService.cs'

if (Test-Path $masterEngine) {
    $engineContent = Get-Content $masterEngine -Raw
    foreach ($token in @('DeckC','DeckD','RecordingSampleProvider','Enum.GetValues<DeckId>')) {
        if ($engineContent -notmatch [regex]::Escape($token)) {
            Add-Failure $failures "Requisito V7 mancante nel motore: $token"
        }
    }
    $waveOutCount = ([regex]::Matches($engineContent, 'new\s+WaveOutEvent\s*\{')).Count
    if ($waveOutCount -ne 2) {
        Add-Failure $failures "Il motore deve contenere due uscite WaveOutEvent separate per master e cuffie; trovate: $waveOutCount"
    }
}

if (Test-Path $v7ViewModel) {
    $v7Content = Get-Content $v7ViewModel -Raw
    foreach ($token in @('StartAutoMashupAsync','LoadDeckCCommand','LoadDeckDCommand','GenericMidiControllerService')) {
        if ($v7Content -notmatch [regex]::Escape($token)) {
            Add-Failure $failures "Funzione V7 mancante: $token"
        }
    }
}

if (-not (Test-Path $midiService)) {
    Add-Failure $failures 'Servizio Generic MIDI mancante.'
}


# 7. Requisiti V8: console tablet, piatti animati, mixer e importazione sicura.
$appProject = Join-Path $rootPath 'NexoraMix.App\NexoraMix.App.csproj'
$remoteViewModel = Join-Path $rootPath 'NexoraMix.App\ViewModels\MainViewModel.Remote.cs'
$remoteIndex = Join-Path $rootPath 'NexoraMix.App\RemoteUi\index.html'
$remoteScript = Join-Path $rootPath 'NexoraMix.App\RemoteUi\app.js'
$mediaImport = Join-Path $rootPath 'NexoraMix.App\ViewModels\MainViewModel.MediaImport.cs'

if (Test-Path $appProject) {
    $projectContent = Get-Content $appProject -Raw
    foreach ($token in @('Microsoft.AspNetCore.App','RemoteUi\**\*')) {
        if ($projectContent -notmatch [regex]::Escape($token)) {
            Add-Failure $failures "Configurazione V8 mancante nel progetto App: $token"
        }
    }
}

if (Test-Path $remoteViewModel) {
    $remoteContent = Get-Content $remoteViewModel -Raw
    foreach ($token in @('TabletRemoteHost','CreateRemoteSnapshot','ExecuteRemoteCommandAsync','deck.targetbpm','deck.saturation')) {
        if ($remoteContent -notmatch [regex]::Escape($token)) {
            Add-Failure $failures "Comando console tablet mancante: $token"
        }
    }
}

if (Test-Path $remoteIndex) {
    $indexContent = Get-Content $remoteIndex -Raw
    foreach ($token in @('platter','LOOP','HOT CUE','EFFETTI','MIXER')) {
        if ($indexContent -notmatch [regex]::Escape($token)) {
            Add-Failure $failures "Elemento UI tablet mancante: $token"
        }
    }
}

if (Test-Path $remoteScript) {
    $scriptContent = Get-Content $remoteScript -Raw
    foreach ($token in @('requestAnimationFrame','deck.seekrelative','global.crossfader','WebSocket')) {
        if ($scriptContent -notmatch [regex]::Escape($token)) {
            Add-Failure $failures "Funzione remota mancante: $token"
        }
    }
}

if (Test-Path $mediaImport) {
    $mediaContent = Get-Content $mediaImport -Raw
    if ($mediaContent -match 'yt5s|turboscribe|youtube-to-mp3|stream.?rip') {
        Add-Failure $failures 'È presente un downloader YouTube di terze parti non consentito.'
    }
    foreach ($token in @('MusicInboxPath','FileSystemWatcher','OpenYouTubeLink')) {
        if ($mediaContent -notmatch [regex]::Escape($token)) {
            Add-Failure $failures "Funzione importazione musica mancante: $token"
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Validazione FALLITA:" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host " - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Validazione statica completata senza errori." -ForegroundColor Green
