<#
.SYNOPSIS
    Gera um JWT de staff para chamar a API do Inventory Service em ambiente LOCAL.

.DESCRIPTION
    O Inventory não emite tokens (o login é do monólito/Identity). Para testar a API na mão (Scalar, curl,
    Invoke-RestMethod) este script assina um JWT HS256 com a mesma chave que a API valida.

    O valor padrão de -Secret é o do appsettings.Development.json, que é uma chave SÓ de desenvolvimento.
    Nunca use a chave real do cluster aqui.

.EXAMPLE
    $token = .\scripts\New-DevToken.ps1
    Invoke-RestMethod http://localhost:5081/api/inventory/parts -Headers @{ Authorization = "Bearer $token" }
#>
param(
    [string]$Secret = 'dev-only-super-secret-change-me-32chars-minimum',
    [string]$Issuer = 'GearFlow.Api',
    [string]$Audience = 'GearFlow.Client',
    [int]$Minutes = 60
)

if ($Secret.Length -lt 32) { throw 'A chave HMAC precisa ter no mínimo 32 caracteres.' }

function ConvertTo-Base64Url([byte[]]$Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$header = '{"alg":"HS256","typ":"JWT"}'
$exp = [DateTimeOffset]::UtcNow.AddMinutes($Minutes).ToUnixTimeSeconds()
$payload = '{{"sub":"{0}","name":"Dev Local","iss":"{1}","aud":"{2}","exp":{3}}}' -f [guid]::NewGuid(), $Issuer, $Audience, $exp

$unsigned = (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))) + '.' +
            (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload)))

$hmac = New-Object System.Security.Cryptography.HMACSHA256 -ArgumentList (, [Text.Encoding]::UTF8.GetBytes($Secret))
$signature = ConvertTo-Base64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned)))

"$unsigned.$signature"
