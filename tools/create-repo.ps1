param([string]$Token)
$ErrorActionPreference = "Stop"
$headers = @{
    Authorization = "Bearer $Token"
    Accept = "application/vnd.github+json"
}
$body = @{
    name = "UWFTool"
    description = "UWF Tool - UWF remote management console for computer labs"
    private = $false
    auto_init = $false
} | ConvertTo-Json

$resp = Invoke-RestMethod -Uri "https://api.github.com/user/repos" -Method Post -Headers $headers -Body $body -ContentType "application/json"
Write-Host ("Created: " + $resp.full_name)
Write-Host ("URL: " + $resp.html_url)
Write-Host ("Clone: " + $resp.clone_url)
