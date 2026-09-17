[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $OmpArguments
)

$ErrorActionPreference = 'Stop'

function ConvertTo-PosixSingleQuoted([string] $Value) {
    $singleQuote = [string][char]39
    $doubleQuote = [string][char]34
    $escapedQuote = $singleQuote + $doubleQuote + $singleQuote + $doubleQuote + $singleQuote
    return $singleQuote + $Value.Replace($singleQuote, $escapedQuote) + $singleQuote
}

function ConvertTo-SandboxPath([string] $HostPath) {
    $absolutePath = [IO.Path]::GetFullPath($HostPath)
    $driveRoot = [IO.Path]::GetPathRoot($absolutePath)
    if ($driveRoot -notmatch '^([a-zA-Z]):\\$') {
        throw "Only drive-letter Windows paths can be mapped into Docker Sandboxes: $absolutePath"
    }
    $drive = $Matches[1].ToLowerInvariant()
    $relativePath = $absolutePath.Substring($driveRoot.Length).Replace('\', '/')
    if ($relativePath) { return "/$drive/$relativePath" }
    return "/$drive"
}

function Find-PaseoSandboxLifecycle([string] $StartPath) {
    $cursor = [IO.DirectoryInfo]::new($StartPath)
    while ($null -ne $cursor) {
        $candidate = Join-Path $cursor.FullName '.paseo\sandbox\lifecycle.mjs'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [pscustomobject]@{ Script = $candidate; Workspace = $cursor.FullName }
        }
        $cursor = $cursor.Parent
    }
    return $null
}

# Paseo probes provider commands outside a workspace. Avoid creating a sandbox
# just to answer the OMP availability check.
if ($OmpArguments -contains '--version') {
    Write-Output 'omp/18.0.4 (Teck Paseo Docker Sandbox)'
    exit 0
}

$cwd = [IO.Path]::GetFullPath((Get-Location).Path)
$lifecycle = Find-PaseoSandboxLifecycle $cwd
if (-not $lifecycle) {
    throw "No .paseo\sandbox\lifecycle.mjs was found above Paseo workspace '$cwd'."
}

$descriptorJson = (& node $lifecycle.Script attach --workspace $lifecycle.Workspace) -join [Environment]::NewLine
if ($LASTEXITCODE -ne 0) {
    throw "Could not attach to the Docker Sandbox for '$($lifecycle.Workspace)'."
}
$descriptor = $descriptorJson | ConvertFrom-Json
$sandboxName = [string]$descriptor.sandboxName
if ($sandboxName -notmatch '^paseo-w-[0-9a-f]{12}$') {
    throw "The lifecycle returned an invalid sandbox name: $sandboxName"
}
$omniRouteBaseUrl = [string]$descriptor.omniRouteBaseUrl
if ($omniRouteBaseUrl -notmatch '^https?://[a-zA-Z0-9.-]+(?::[0-9]+)?/v1/?$') {
    throw "The lifecycle returned an invalid OmniRoute base URL: $omniRouteBaseUrl"
}
$remoteGitDir = [string]$descriptor.remoteGitDir
if ($remoteGitDir -notmatch '^/[a-z]/') {
    throw "The lifecycle returned an invalid Git directory: $remoteGitDir"
}

$remoteCwd = ConvertTo-SandboxPath $cwd
$remoteWorkspace = [string]$descriptor.remoteWorkspacePath
if ($remoteWorkspace -notmatch '^/[a-z]/') {
    throw "The lifecycle returned an invalid workspace path: $remoteWorkspace"
}

# Keep gh CLI authenticated inside the sandbox. Docker Sandbox credentials may
# expose GITHUB_TOKEN as a proxy-managed sentinel; for gh CLI to receive a real
# token, configure the host with:
#   sbx secret set github --command 'gh auth token'
$ghSync = 'install -d -m 700 ~/.config/gh && ' +
    "printf 'version: 1\n' > ~/.config/gh/config.yml && " +
    'token=$GITHUB_TOKEN && ' +
    '[ -z "$token" ] && token=$GH_TOKEN && ' +
    '[ -n "$token" ] && printf ''github.com:\n    oauth_token: %s\n    user: %s\n'' "$token" "${GITHUB_USER:-CaptainPowerTurtle}" > ~/.config/gh/hosts.yml && chmod 600 ~/.config/gh/hosts.yml'

$quotedArgs = @($OmpArguments | ForEach-Object { ConvertTo-PosixSingleQuoted $_ })
$agentCommand = 'cd -- ' + (ConvertTo-PosixSingleQuoted $remoteCwd) + ' && ' + $ghSync + ' && exec /usr/local/bin/omp'
if ($quotedArgs.Count -gt 0) {
    $agentCommand += ' ' + ($quotedArgs -join ' ')
}
$remoteCommand = 'exec /usr/bin/sudo -H -u agent ' +
    '--preserve-env=HTTP_PROXY,HTTPS_PROXY,ALL_PROXY,NO_PROXY,http_proxy,https_proxy,all_proxy,no_proxy ' +
    '-- /usr/bin/env ' +
    'HOME=/home/agent ' +
    'PATH=/usr/local/share/npm-global/bin:/usr/local/bin:/usr/bin:/bin ' +
    "GIT_DIR=$(ConvertTo-PosixSingleQuoted $remoteGitDir) " +
    "GIT_WORK_TREE=$(ConvertTo-PosixSingleQuoted $remoteWorkspace) " +
    'OMNIROUTE_API_KEY=proxy-managed ' +
    "OMNIROUTE_BASE_URL=$(ConvertTo-PosixSingleQuoted $omniRouteBaseUrl) " +
    'OMNIROUTE_MODEL=teck-orchestrator ' +
    'OMNIROUTE_RESEARCH_ENABLED=1 ' +
    'OMP_SKIP_SETUP=1 ' +
    'ONNXRUNTIME_NODE_INSTALL=skip ' +
    'PUPPETEER_EXECUTABLE_PATH=/usr/bin/google-chrome-stable ' +
    'PUPPETEER_PROXY=http://gateway.docker.internal:3128 ' +
    'PUPPETEER_PROXY_IGNORE_CERT_ERRORS=true ' +
    '/bin/sh -c ' + (ConvertTo-PosixSingleQuoted $agentCommand)

# No TTY: OMP RPC requires an unmodified JSONL stream on stdin/stdout.
& ssh.exe -T -o BatchMode=yes -o LogLevel=ERROR -- "$sandboxName.sbx" $remoteCommand
exit $LASTEXITCODE
