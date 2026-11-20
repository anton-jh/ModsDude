<#
.SYNOPSIS
    Generates the certificates the server signs and encrypts sign-in tokens with.

.DESCRIPTION
    Prints four lines for the server's .env: each certificate as a base64 PFX, and the random password
    that protects it. Nothing is written to disk.

    The certificates are self-signed RSA 4096 and valid for two years. Replacing them signs every user
    out, because the tokens already issued can no longer be read.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function New-TokenCertificate([string]$Name, [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]$Usage) {
    $rsa = [System.Security.Cryptography.RSA]::Create(4096)

    try {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            "CN=ModsDude $Name",
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

        $request.CertificateExtensions.Add(
            [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($Usage, $true))

        $now = [DateTimeOffset]::UtcNow
        $certificate = $request.CreateSelfSigned($now, $now.AddYears(2))

        try {
            $password = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
            $pfx = $certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $password)

            [pscustomobject]@{
                Certificate = [Convert]::ToBase64String($pfx)
                Password    = $password
            }
        }
        finally {
            $certificate.Dispose()
        }
    }
    finally {
        $rsa.Dispose()
    }
}

$signing = New-TokenCertificate 'token signing' ([System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature)
$encryption = New-TokenCertificate 'token encryption' ([System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment)

"TOKEN_SIGNING_CERTIFICATE=$($signing.Certificate)"
"TOKEN_SIGNING_CERTIFICATE_PASSWORD=$($signing.Password)"
"TOKEN_ENCRYPTION_CERTIFICATE=$($encryption.Certificate)"
"TOKEN_ENCRYPTION_CERTIFICATE_PASSWORD=$($encryption.Password)"
