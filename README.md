# Network Monitor

App nativo para Windows 11 que mostra **quem está conectado aonde** e um **overlay** com a taxa de download/upload.

## O que faz

- Lista IPs remotos em uso (TCP, IPv4 e IPv6)
- Mostra qual aplicativo/PID está usando cada IP
- Agrupa a visão por aplicativo, por IP ou por conexão
- Overlay compacto com download, upload, conexões ativas e IP externo (cada item pode ser ocultado)
- Gráfico de tráfego no programa (linha, área ou barras)
- Teste de DNS e bloqueio de aplicativo/IP pelo Firewall do Windows
- Histórico de conexões encerradas (5, 10 ou 30 minutos)
- Alerta visual e na bandeja quando há pico de consumo
- Ícone na bandeja do sistema (fechar a janela não encerra o app)
- Filtros: só conexões estabelecidas, ocultar LAN/loopback, UDP, DNS reverso
- Exportar a lista para CSV
- Iniciar com o Windows

## Como usar

1. Rode `dist\NetworkMonitor.exe` (ou compile com o script abaixo). A publicação padrão é em pasta, não um EXE único, para o Windows SmartScreen não bloquear o arquivo.
2. Arraste o overlay para o canto da tela. Clique nele para abrir a janela principal.
3. Botão direito no overlay ou na bandeja: esconder, compactar, sair.

Alguns nomes de processo do sistema só aparecem se o app for executado como administrador. A lista de IPs continua funcionando sem elevação.

O overlay aparece sobre janelas em modo janela/borderless. Jogos em fullscreen exclusivo cobrem qualquer overlay.

## Compilar

Requer o SDK .NET 8 (Windows Desktop).

```powershell
.\scripts\build.ps1
```

O app fica em `dist\NetworkMonitor.exe` junto com as DLLs. Para um único arquivo (mais sujeito a SmartScreen):

```powershell
.\scripts\build.ps1 -SingleFile
```

## Atualizacao automatica (GitHub Releases)

O app consulta `https://github.com/cirobrandao/network-monitor/releases/latest` na abertura e pelo botao **Atualizar**.

- Nao precisa de servidor proprio: usa a CDN do GitHub.
- Nao usa `git` no PC do usuario.
- Pacote esperado na release: `NetworkMonitor-win-x64.zip` (conteudo da pasta `dist\`).

### Publicar uma versao

```powershell
# exige gh autenticado
.\scripts\release.ps1 -Version 1.1.0
```

Isso compila, gera o zip, cria a tag `v1.1.0` e a release com o asset.
