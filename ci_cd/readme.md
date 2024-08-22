## CI/CD

Build and deploy automatization

Application is composed of both frontend and backend, which both have different way of building and need to compose the plugin in specific structure.

Ansible playbooks are used to do those tasks.

Ansible is quite hard to run on Windows, so I made docker container for controller and container with tools needed to build frontend. To build backend docker host (Windows) is used via ssh connection as one of the ansible nodes.


### ISSUES

#### TASK [Gathering Facts] on WIN fails on Access denied for Get-CimInstance:

see. https://github.com/PowerShell/Win32-OpenSSH/issues/2077 <br/>
workaround: https://github.com/microsoft/vscode-remote-release/issues/2648#issuecomment-1293832539
