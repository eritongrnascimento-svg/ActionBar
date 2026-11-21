# ActionBar para Linux

A edição Linux usa Python e Qt e inclui busca de aplicativos e arquivos, calculadora, YouTube, Internet, música, terminal interativo, OCR, tradução, notificações, lembretes, pastas, wallpapers e consulta de atualizações. O terminal usa um PTY e pode receber respostas, sinais e comandos na mesma sessão.

## Instalação x86-64

Use o pacote **.deb** no Ubuntu ou derivados Debian. O APT resolve as dependências nativas:

    sudo apt install ./actionbar_0.6.4_amd64.deb

Use o **.run** para instalar na conta atual, inclusive em Arch Linux, Fedora e openSUSE. O instalador coloca o programa no menu de aplicativos e cria ~/.local/bin/actionbar:

    sh ActionBar-0.6.4-Linux-x64.run

Os dois pacotes incluem Qt/PySide6 6.10.2, dbus-next e xterm.js. Requerem Python 3.10–3.14, glibc 2.34 ou mais recente e as bibliotecas nativas normalmente presentes em ambientes gráficos. O pacote .run também requer Tesseract para OCR e wmctrl para gerenciar outras janelas no X11.

| Ambiente | Consulta de atualizações | Instalação de OCR |
| --- | --- | --- |
| Ubuntu | apt list --upgradable | sudo apt install tesseract-ocr tesseract-ocr-por tesseract-ocr-eng |
| Arch Linux | pacman -Qu | sudo pacman -S tesseract tesseract-data-por tesseract-data-eng |
| Fedora | dnf5 check-upgrade ou dnf check-update | sudo dnf install tesseract tesseract-langpack-por tesseract-langpack-eng |
| openSUSE | zypper --no-refresh list-updates | sudo zypper install tesseract-ocr tesseract-ocr-traineddata-portuguese tesseract-ocr-traineddata-english |

## Diferenças entre ambientes gráficos

- **Plasma, GNOME e Cinnamon** ganham ações de configurações específicas. Wallpapers são aplicados pelo comando do Plasma ou pelos esquemas GSettings do GNOME e Cinnamon.
- **OCR** usa captura local com seleção de área no X11. Em Wayland, o portal do ambiente gráfico controla o diálogo e as opções de captura. Também é possível ler um arquivo de imagem.
- **Janelas e grupos** podem ser focalizados, encaixados e restaurados em X11 com wmctrl. A restauração reposiciona janelas abertas. Wayland restringe o controle geral de janelas de outros apps.
- **Notificações** podem acompanhar mensagens novas se o barramento D-Bus permitir monitoramento. O painel não recupera notificações anteriores nem notificações entregues por interfaces privadas do ambiente.
- **Atalho global.** Cadastre actionbar como atalho personalizado do desktop para mostrar ou ocultar a instância existente. O aplicativo mantém a consulta limpa quando é reaberto.
- **Busca por termo no YouTube** requer ACTIONBAR_YOUTUBE_API_KEY. Links de vídeos, lives e Shorts são aceitos. O próprio provedor decide se o conteúdo pode ser incorporado.
- **Tradução automática por Azure** requer ACTIONBAR_TRANSLATOR_KEY e, para recursos regionais, ACTIONBAR_TRANSLATOR_REGION. O Google Tradutor pode abrir no painel Internet sem chave. Use Configurações para escolher a pasta inicial, os volumes visíveis e a centralização em X11. Nas exceções de centralização, informe as classes mostradas na aba Janelas.
- **Lembretes** ficam no diretório XDG de dados do usuário. Para disparar pontualmente, deixe o ActionBar em execução.

O porte ainda não tem paridade completa com o Windows. Faltam a reabertura automática de aplicativos dos grupos, toda a personalização WPF, os widgets específicos do Windows, alguns atalhos e ações de sistema, conversões completas da calculadora e a leitura portátil da central de notificações. O funcionamento varia conforme desktop, compositor e pacotes instalados.

## Desenvolvimento

A partir da raiz do repositório:

    python3 -m venv .venv
    .venv/bin/python -m pip install -r linux/requirements.txt
    .venv/bin/python linux/actionbar.py

Os scripts de empacotamento estão em packaging/. Para reproduzir a edição 0.6.4, obtenha os cinco wheels Linux x86-64 e universais de linux/requirements.txt, em seguida execute:

    python3 packaging/build-linux.py --wheels wheelhouse --out dist

## Referências oficiais

[Ubuntu APT](https://ubuntu.com/server/docs/how-to/software/package-management/) · [Arch pacman](https://man.archlinux.org/man/pacman.8) · [Fedora DNF5](https://dnf5.readthedocs.io/en/stable/commands/check-upgrade.8.html) · [openSUSE Zypper](https://doc.opensuse.org/documentation/tumbleweed/zypper/) · [Plasma](https://docs.kde.org/stable_kf6/en/systemsettings/systemsettings/systemsettings.pdf) · [GNOME](https://github.com/GNOME/gnome-control-center/blob/main/README.md) · [Cinnamon](https://github.com/linuxmint/cinnamon-control-center/blob/master/generate_additional_files.py) · [freedesktop.org](https://specifications.freedesktop.org/notification/latest-single/)

