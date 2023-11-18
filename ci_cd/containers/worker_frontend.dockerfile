FROM python:3.13-rc-alpine3.18
USER root

RUN apk update && apk upgrade && apk add \
    npm \
    openssh

COPY ci_cd/data/worker_frontend_entrypoint.sh /entrypoint.sh
# Copy for local dev / otherwise should be git clone
# nmp modules must be included in image - if in volume build is slowed dramatically
COPY frontend/sdpi_components/sdpi-components /opt/workplace

RUN chmod +x /entrypoint.sh && \
    echo 'PermitRootLogin=yes' | tee -a /etc/ssh/sshd_config && \
    echo 'PasswordAuthentication yes' >> /etc/ssh/sshd_config && \
    adduser -h /home/ansible -s /bin/sh -D ansible && \
    echo -n 'ansible:ansible' | chpasswd && \
    chown -R ansible:ansible /opt/workplace

USER ansible

RUN  cd /opt/workplace && npm install

USER root

EXPOSE 22

WORKDIR /opt/workplace

ENTRYPOINT ["/entrypoint.sh"]