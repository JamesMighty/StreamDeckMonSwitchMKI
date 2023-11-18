#!/bin/sh

ansible-playbook -v -i ./inventory "$@"
