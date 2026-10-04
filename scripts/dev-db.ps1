<#
.SYNOPSIS
  Banco PostgreSQL local descartável para desenvolvimento e testes, sem Docker.

.DESCRIPTION
  Usa os binários de um PostgreSQL já instalado (initdb/pg_ctl) para criar um cluster PRIVADO dentro de .tools/
  (ignorado pelo Git), em outra porta (padrão 54329), com autenticação "trust" apenas em localhost.
  Não toca no serviço PostgreSQL do sistema nem em nenhuma configuração dele.

  Quem tem Docker pode usar:  docker compose up -d db   (mesma porta, mesma conexão)

.EXAMPLE
  ./scripts/dev-db.ps1            # sobe (cria o cluster na primeira vez)
  ./scripts/dev-db.ps1 stop       # para
  ./scripts/dev-db.ps1 status
  ./scripts/dev-db.ps1 reset      # apaga tudo e recria do zero
#>
[CmdletBinding()]
param(
    [ValidateSet('start', 'stop', 'status', 'reset')]
    [string]$Action = 'start',

    [int]$Port = 54329,

    # Pasta "bin" do PostgreSQL. Se omitida, procura em C:\Program Files\PostgreSQL\<versão>\bin.
    [string]$PgBin = $env:PG_BIN
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $repo '.tools'
$data = Join-Path $tools 'pgdata'
$log = Join-Path $tools 'postgres.log'
$database = 'ronat_dev'

function Find-PgBin {
    if ($PgBin -and (Test-Path (Join-Path $PgBin 'pg_ctl.exe'))) { return $PgBin }

    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $base) { continue }
        $pgRoot = Join-Path $base 'PostgreSQL'
        if (-not (Test-Path $pgRoot)) { continue }
        $versions = Get-ChildItem $pgRoot -Directory |
            Sort-Object { [int](($_.Name -replace '\D', '') + '0') } -Descending
        foreach ($version in $versions) {
            $bin = Join-Path $version.FullName 'bin'
            if (Test-Path (Join-Path $bin 'pg_ctl.exe')) { return $bin }
        }
    }

    throw 'PostgreSQL não encontrado. Instale o PostgreSQL 16+, informe -PgBin (ou a variável PG_BIN) ou use: docker compose up -d db'
}

$bin = Find-PgBin

function Invoke-Pg([string]$Exe, [string[]]$Arguments) {
    & (Join-Path $bin "$Exe.exe") @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe falhou (código $LASTEXITCODE)" }
}

function Test-Running {
    & (Join-Path $bin 'pg_ctl.exe') -D $data status *> $null
    return ($LASTEXITCODE -eq 0)
}

function Start-Db {
    New-Item -ItemType Directory -Force $tools | Out-Null

    if (-not (Test-Path (Join-Path $data 'PG_VERSION'))) {
        Write-Host "Criando o cluster em $data ..."
        Invoke-Pg 'initdb' @('-D', $data, '-U', 'postgres', '--auth=trust', '-E', 'UTF8', '--no-locale')
    }

    if (Test-Running) {
        Write-Host 'O PostgreSQL local já está rodando.'
    }
    else {
        # O servidor é criado via WMI (Win32_Process.Create) para NÃO herdar os handles do terminal de quem chamou
        # o script. Do contrário, ferramentas e CI que capturam a saída ficariam esperando para sempre.
        $pgctl = Join-Path $bin 'pg_ctl.exe'
        $commandLine = "`"$pgctl`" -D `"$data`" -o `"-p $Port -c listen_addresses=localhost`" -l `"$log`" -w start"
        $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $commandLine; CurrentDirectory = $tools }
        if ($created.ReturnValue -ne 0) { throw "Não foi possível iniciar o pg_ctl (WMI retornou $($created.ReturnValue))." }

        $deadline = (Get-Date).AddSeconds(40)
        do {
            Start-Sleep -Milliseconds 400
            & (Join-Path $bin 'pg_isready.exe') -h localhost -p $Port *> $null
            $ready = ($LASTEXITCODE -eq 0)
        } until ($ready -or (Get-Date) -gt $deadline)
        if (-not $ready) {
            if (Test-Path $log) { Get-Content $log -Tail 20 | ForEach-Object { Write-Host $_ } }
            throw "O PostgreSQL local não respondeu em 40 s. Veja $log"
        }
    }

    $exists = & (Join-Path $bin 'psql.exe') -h localhost -p $Port -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$database'"
    if (-not $exists) {
        Invoke-Pg 'createdb' @('-h', 'localhost', '-p', "$Port", '-U', 'postgres', $database)
    }

    Write-Host ''
    Write-Host 'PostgreSQL local pronto. Conexão de desenvolvimento:'
    Write-Host "  Host=localhost;Port=$Port;Database=$database;Username=postgres;Password=postgres"
    Write-Host 'Testes de integração (TEST_DATABASE_URL, padrão):'
    Write-Host "  Host=localhost;Port=$Port;Username=postgres;Password=postgres"
}

function Stop-Db {
    if (Test-Running) {
        Invoke-Pg 'pg_ctl' @('-D', $data, '-m', 'fast', '-w', 'stop')
    }
    else {
        Write-Host 'O PostgreSQL local não está rodando.'
    }
}

switch ($Action) {
    'start' { Start-Db }
    'stop' { Stop-Db }
    'status' { & (Join-Path $bin 'pg_ctl.exe') -D $data status }
    'reset' {
        Stop-Db
        if (Test-Path $data) { Remove-Item $data -Recurse -Force }
        Start-Db
    }
}
