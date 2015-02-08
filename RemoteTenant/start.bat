start cmd /c @powershell -Command "cd .\src\TenantRegistry; $pwd; kvm use default; while ($true) { cmd /c k --watch web }"
start cmd /c @powershell -Command "cd .\src\RemoteTenant; $pwd; kvm use default; while ($true) { cmd /c k --watch web1 }"
start cmd /c @powershell -Command "cd .\src\RemoteTenant; $pwd; kvm use default; while ($true) { cmd /c k --watch web2 }"
start cmd /c @powershell -Command "cd .\src\RemoteTenant; $pwd; kvm use default; while ($true) { cmd /c k --watch web3 }"
