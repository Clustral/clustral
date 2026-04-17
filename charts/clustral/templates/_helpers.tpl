{{/*
Expand the name of the chart.
*/}}
{{- define "clustral.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "clustral.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Chart label value.
*/}}
{{- define "clustral.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels.
*/}}
{{- define "clustral.labels" -}}
helm.sh/chart: {{ include "clustral.chart" . }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/version: {{ .Values.global.appVersion | default .Chart.AppVersion | quote }}
{{- end }}

{{/*
Selector labels for a specific component.
Usage: {{ include "clustral.selectorLabels" (dict "root" . "component" "controlplane") }}
*/}}
{{- define "clustral.selectorLabels" -}}
app.kubernetes.io/name: {{ include "clustral.name" .root }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/component: {{ .component }}
{{- end }}

{{/*
Image tag — defaults to Chart.AppVersion.
*/}}
{{- define "clustral.imageTag" -}}
{{- .tag | default .chartAppVersion }}
{{- end }}

{{/*
MongoDB connection string.
If the Bitnami subchart is enabled, build from the release-named service.
Otherwise, use the external connection string.
*/}}
{{- define "clustral.mongoConnectionString" -}}
{{- if .Values.mongodb.enabled -}}
mongodb://{{ .Release.Name }}-mongodb:27017
{{- else -}}
{{ .Values.externalMongodb.connectionString }}
{{- end -}}
{{- end }}

{{/*
RabbitMQ host.
*/}}
{{- define "clustral.rabbitmqHost" -}}
{{- if .Values.rabbitmq.enabled -}}
{{ .Release.Name }}-rabbitmq
{{- else -}}
{{ .Values.externalRabbitmq.host }}
{{- end -}}
{{- end }}

{{/*
RabbitMQ port.
*/}}
{{- define "clustral.rabbitmqPort" -}}
{{- if .Values.rabbitmq.enabled -}}
5672
{{- else -}}
{{ .Values.externalRabbitmq.port }}
{{- end -}}
{{- end }}

{{/*
RabbitMQ user.
*/}}
{{- define "clustral.rabbitmqUser" -}}
{{- if .Values.rabbitmq.enabled -}}
{{ .Values.rabbitmq.auth.username }}
{{- else -}}
{{ .Values.externalRabbitmq.user }}
{{- end -}}
{{- end }}

{{/*
RabbitMQ password.
*/}}
{{- define "clustral.rabbitmqPass" -}}
{{- if .Values.rabbitmq.enabled -}}
{{ .Values.rabbitmq.auth.password }}
{{- else -}}
{{ .Values.externalRabbitmq.pass }}
{{- end -}}
{{- end }}

{{/*
OIDC authority.
*/}}
{{- define "clustral.oidcAuthority" -}}
{{ .Values.oidc.authority }}
{{- end }}

{{/*
OIDC metadata address — defaults to authority + /.well-known/openid-configuration.
*/}}
{{- define "clustral.oidcMetadataAddress" -}}
{{- if .Values.oidc.metadataAddress -}}
{{ .Values.oidc.metadataAddress }}
{{- else -}}
{{ printf "%s/.well-known/openid-configuration" .Values.oidc.authority }}
{{- end -}}
{{- end }}

{{/*
Secret key names — cert-manager uses tls.crt/tls.key, manual uses ca.crt/ca.key.
*/}}
{{- define "clustral.caSecretCertKey" -}}
{{- if .Values.certManager.enabled -}}tls.crt{{- else -}}ca.crt{{- end -}}
{{- end }}

{{- define "clustral.caSecretKeyKey" -}}
{{- if .Values.certManager.enabled -}}tls.key{{- else -}}ca.key{{- end -}}
{{- end }}

{{/*
JWT secret key names — cert-manager uses tls.crt/tls.key, manual uses public.pem/private.pem.
*/}}
{{- define "clustral.jwtSecretPrivateKey" -}}
{{- if .Values.certManager.enabled -}}tls.key{{- else -}}private.pem{{- end -}}
{{- end }}

{{- define "clustral.jwtSecretPublicKey" -}}
{{- if .Values.certManager.enabled -}}tls.crt{{- else -}}public.pem{{- end -}}
{{- end }}

{{/*
Validation — ingress and gatewayApi are mutually exclusive.
*/}}
{{- define "clustral.validateExclusiveIngress" -}}
{{- if and .Values.ingress.enabled .Values.gatewayApi.enabled -}}
{{- fail "ingress.enabled and gatewayApi.enabled are mutually exclusive — enable only one" -}}
{{- end -}}
{{- end }}
