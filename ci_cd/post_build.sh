#!/usr/bin/env sh

PROJECT_ROOT="/mnt/i/_devel/csharp/StreamDeckMonSwitch/StreamDeckMonSwitchMKI/StreamDeckMonSwitchMKI"

cd "${PROJECT_ROOT}/frontend/sdpi_components/sdpi-components"
ansible-playbook -v  "${PROJECT_ROOT}/ci_cd/playbooks/post_backend_build.yml"