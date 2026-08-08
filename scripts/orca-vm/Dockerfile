ARG DEVCONTAINER_IMAGE=teck-devcontainer:orca-source
FROM ${DEVCONTAINER_IMAGE}

# The container entrypoint starts sshd, which must retain root in order to
# perform privilege separation and create the vscode login session.
# nosemgrep: dockerfile.security.last-user-is-root.last-user-is-root
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends openssh-server \
    && mkdir -p /run/sshd /home/vscode/.ssh \
    && chmod 700 /home/vscode/.ssh \
    && chown -R vscode:vscode /home/vscode/.ssh \
    && ssh-keygen -A \
    && rm -rf /var/lib/apt/lists/*

COPY scripts/orca-vm/ssh-entrypoint.sh /usr/local/bin/orca-docker-ssh-entrypoint
RUN chmod 0755 /usr/local/bin/orca-docker-ssh-entrypoint

ENTRYPOINT ["/usr/local/bin/orca-docker-ssh-entrypoint"]
