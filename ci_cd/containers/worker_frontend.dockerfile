FROM python:3.13-rc-alpine3.18

USER root

RUN apk update && apk upgrade && apk add \
    npm \
    openssh

COPY ci_cd/data/worker_frontend_entrypoint.sh /entrypoint.sh

RUN chmod +x /entrypoint.sh && \
    echo 'PermitRootLogin=yes' | tee -a /etc/ssh/sshd_config && \
    echo 'PasswordAuthentication yes' >> /etc/ssh/sshd_config && \
    adduser -h /home/ansible -s /bin/sh -D ansible && \
    echo -n 'ansible:ansible' | chpasswd && \
    mkdir /opt/workplace && chown -R ansible:ansible /opt/workplace && \
    mkdir /home/ansible/.npm && chown -R 1000:1000 /home/ansible/.npm

EXPOSE 22

WORKDIR /opt/workplace

ENTRYPOINT ["/entrypoint.sh"]