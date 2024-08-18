#!/bin/sh

ansible-playbook -v -i /ansible/inventory/inventory.yml -i /ansible/inventory/secrets.yml "$@"
