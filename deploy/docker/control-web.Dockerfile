FROM nginx:1.27-alpine

COPY deploy/nginx/control-web.conf /etc/nginx/conf.d/default.conf
COPY src/Crystal.Control.Web/ /usr/share/nginx/html/

EXPOSE 80
