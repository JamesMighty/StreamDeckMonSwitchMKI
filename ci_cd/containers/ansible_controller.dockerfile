FROM alpine:3.18.3

RUN apk add --no-cache \
		bzip2 \
		file \
		gzip \
		libffi \
		libffi-dev \
		krb5 \
		krb5-dev \
		krb5-libs \
		musl-dev \
		openssh \
		openssl-dev \
		python3-dev \
		py3-cffi \
		py3-cryptography \
		py3-setuptools \
        py3-pip \
		sshpass \
		tar \
		rsync \
		&& \
	apk add --no-cache --virtual build-dependencies \
		gcc \
		make

RUN python3 -m ensurepip --upgrade \
		&& \
	pip3 install \
		ansible \
		botocore \
		boto3 \
		awscli\
		pywinrm[kerberos]\
		&& \
	apk del build-dependencies \
		&& \
	rm -rf /root/.cache

RUN pip3 install --upgrade pip && \
    mkdir -p /etc/ansible/ /ansible /ansible/playbooks

ENV ANSIBLE_GATHERING=smart \
    ANSIBLE_HOST_KEY_CHECKING=false \
    ANSIBLE_RETRY_FILES_ENABLED=false \
    ANSIBLE_ROLES_PATH=/ansible/playbooks/roles \
    ANSIBLE_SSH_PIPELINING=True \
    PYTHONPATH=/ansible/lib \
    PATH=/ansible/bin:$PATH \
    ANSIBLE_LIBRARY=/ansible/library \
    EDITOR=nano

RUN ansible-galaxy collection install ansible.posix
RUN ansible-galaxy collection install ansible.windows

WORKDIR /ansible/playbooks

EXPOSE 22
EXPOSE 830

COPY ci_cd/data/ansible_controller_entrypoint.sh /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]