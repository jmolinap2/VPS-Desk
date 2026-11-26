# Servidor de prueba para la E2E de resiliencia: Debian con sshd, git y un «docker» simulado (docker-shim.sh).
# Solo para uso local de pruebas; la contraseña es de usar y tirar.
FROM debian:12-slim
RUN apt-get update \
 && apt-get install -y --no-install-recommends openssh-server git ca-certificates procps \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /run/sshd \
 && useradd -m -s /bin/sh deploy \
 && echo 'deploy:deploy-pass' | chpasswd \
 && sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication yes/' /etc/ssh/sshd_config
COPY docker-shim.sh /usr/local/bin/docker
RUN chmod +x /usr/local/bin/docker \
 && mkdir -p /srv/app \
 && chown deploy:deploy /srv/app
USER deploy
RUN cd /srv/app && git init -q -b main \
 && git config user.email e2e@example.invalid && git config user.name e2e \
 && printf 'services:\n  app:\n    image: example\n' > compose.yml \
 && git add compose.yml && git commit -q -m "e2e"
USER root
EXPOSE 22
CMD ["/usr/sbin/sshd", "-D", "-e"]
