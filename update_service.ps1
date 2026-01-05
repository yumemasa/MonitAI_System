# Stop the service
Stop-Service -Name MonitAI_Service -Force -ErrorAction SilentlyContinue

# Wait a bit
Start-Sleep -Seconds 2

# Start the service
Start-Service -Name MonitAI_Service

Write-Host "Service restarted."
