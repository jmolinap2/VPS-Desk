from __future__ import annotations

import sys
from datetime import date
from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (
    BaseDocTemplate,
    Frame,
    KeepTogether,
    PageBreak,
    PageTemplate,
    Paragraph,
    Spacer,
    Table,
    TableStyle,
)


NAVY = colors.HexColor("#17233B")
BLUE = colors.HexColor("#2D6CDF")
CYAN = colors.HexColor("#47C2D2")
PALE = colors.HexColor("#EEF4FF")
LIGHT = colors.HexColor("#F6F8FC")
TEXT = colors.HexColor("#253047")
MUTED = colors.HexColor("#657089")
WARNING = colors.HexColor("#FFF3D6")
WARNING_BORDER = colors.HexColor("#D39100")


def build_styles():
    base = getSampleStyleSheet()
    return {
        "title": ParagraphStyle(
            "Title",
            parent=base["Title"],
            fontName="Helvetica-Bold",
            fontSize=29,
            leading=34,
            textColor=colors.white,
            alignment=TA_LEFT,
            spaceAfter=8,
        ),
        "subtitle": ParagraphStyle(
            "Subtitle",
            parent=base["Normal"],
            fontName="Helvetica",
            fontSize=12,
            leading=18,
            textColor=colors.HexColor("#DCE7FF"),
        ),
        "h1": ParagraphStyle(
            "H1",
            parent=base["Heading1"],
            fontName="Helvetica-Bold",
            fontSize=20,
            leading=24,
            textColor=NAVY,
            spaceBefore=4,
            spaceAfter=10,
        ),
        "h2": ParagraphStyle(
            "H2",
            parent=base["Heading2"],
            fontName="Helvetica-Bold",
            fontSize=12.5,
            leading=16,
            textColor=BLUE,
            spaceBefore=10,
            spaceAfter=5,
        ),
        "body": ParagraphStyle(
            "Body",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.6,
            leading=14.2,
            textColor=TEXT,
            spaceAfter=7,
        ),
        "small": ParagraphStyle(
            "Small",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=8.2,
            leading=11.5,
            textColor=MUTED,
        ),
        "step": ParagraphStyle(
            "Step",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.5,
            leading=13.5,
            textColor=TEXT,
            leftIndent=10,
            firstLineIndent=-10,
            spaceAfter=5,
        ),
        "code": ParagraphStyle(
            "Code",
            parent=base["Code"],
            fontName="Courier",
            fontSize=8.2,
            leading=11,
            textColor=NAVY,
        ),
        "callout": ParagraphStyle(
            "Callout",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=9.2,
            leading=13.5,
            textColor=TEXT,
        ),
        "toc": ParagraphStyle(
            "TOC",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=10,
            leading=18,
            textColor=TEXT,
        ),
        "table_head": ParagraphStyle(
            "TableHead",
            parent=base["BodyText"],
            fontName="Helvetica-Bold",
            fontSize=8.2,
            leading=10.5,
            textColor=colors.white,
        ),
        "table_cell": ParagraphStyle(
            "TableCell",
            parent=base["BodyText"],
            fontName="Helvetica",
            fontSize=8.2,
            leading=10.5,
            textColor=TEXT,
        ),
        "table_cell_bold": ParagraphStyle(
            "TableCellBold",
            parent=base["BodyText"],
            fontName="Helvetica-Bold",
            fontSize=8.2,
            leading=10.5,
            textColor=TEXT,
        ),
    }


def page_chrome(canvas, doc):
    canvas.saveState()
    width, height = A4
    canvas.setFillColor(NAVY)
    canvas.rect(0, height - 15 * mm, width, 15 * mm, fill=1, stroke=0)
    canvas.setFillColor(colors.white)
    canvas.setFont("Helvetica-Bold", 8.5)
    canvas.drawString(18 * mm, height - 9.5 * mm, "VPS DESK  /  MANUAL DE USUARIO")
    canvas.setStrokeColor(colors.HexColor("#DDE4F0"))
    canvas.line(18 * mm, 15 * mm, width - 18 * mm, 15 * mm)
    canvas.setFillColor(MUTED)
    canvas.setFont("Helvetica", 7.5)
    canvas.drawString(18 * mm, 9.5 * mm, "Edición portable e instalable para Windows x64")
    canvas.drawRightString(width - 18 * mm, 9.5 * mm, f"Página {doc.page}")
    canvas.restoreState()


def title_page(canvas, doc):
    canvas.saveState()
    width, height = A4
    canvas.setFillColor(NAVY)
    canvas.rect(0, 0, width, height, fill=1, stroke=0)
    canvas.setFillColor(BLUE)
    canvas.circle(width - 22 * mm, height - 22 * mm, 44 * mm, fill=1, stroke=0)
    canvas.setFillColor(CYAN)
    canvas.circle(width - 3 * mm, height - 57 * mm, 25 * mm, fill=1, stroke=0)
    canvas.setFillColor(colors.HexColor("#203252"))
    canvas.rect(0, 0, width, 52 * mm, fill=1, stroke=0)
    canvas.restoreState()


def callout(text, style, background=PALE, border=BLUE):
    table = Table([[Paragraph(text, style)]], colWidths=[166 * mm])
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), background),
                ("BOX", (0, 0), (-1, -1), 0.8, border),
                ("LEFTPADDING", (0, 0), (-1, -1), 9),
                ("RIGHTPADDING", (0, 0), (-1, -1), 9),
                ("TOPPADDING", (0, 0), (-1, -1), 8),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 8),
            ]
        )
    )
    return table


def step(number, text, styles):
    return Paragraph(f"<b>{number}.</b> {text}", styles["step"])


def path_box(path, styles):
    table = Table([[Paragraph(path, styles["code"])]], colWidths=[166 * mm])
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, -1), LIGHT),
                ("BOX", (0, 0), (-1, -1), 0.5, colors.HexColor("#CDD6E5")),
                ("LEFTPADDING", (0, 0), (-1, -1), 8),
                ("RIGHTPADDING", (0, 0), (-1, -1), 8),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ]
        )
    )
    return table


def wrap_table_rows(rows, styles):
    wrapped = []
    for row_index, row in enumerate(rows):
        wrapped_row = []
        for column_index, value in enumerate(row):
            style = styles["table_head"] if row_index == 0 else (
                styles["table_cell_bold"] if column_index == 0 else styles["table_cell"]
            )
            wrapped_row.append(Paragraph(value, style))
        wrapped.append(wrapped_row)
    return wrapped


def make_manual(output_path: Path):
    output_path.parent.mkdir(parents=True, exist_ok=True)
    styles = build_styles()
    width, height = A4
    frame = Frame(18 * mm, 19 * mm, width - 36 * mm, height - 39 * mm, id="body")
    cover_frame = Frame(20 * mm, 42 * mm, width - 40 * mm, height - 74 * mm, id="cover")
    doc = BaseDocTemplate(
        str(output_path),
        pagesize=A4,
        leftMargin=18 * mm,
        rightMargin=18 * mm,
        topMargin=22 * mm,
        bottomMargin=19 * mm,
        title="VPS Desk - Manual de usuario",
        author="VPS Desk",
        subject="Uso portable, instalación y conexión SSH",
    )
    doc.addPageTemplates(
        [
            PageTemplate(id="Cover", frames=[cover_frame], onPage=title_page),
            PageTemplate(id="Body", frames=[frame], onPage=page_chrome),
        ]
    )

    story = []
    story.append(Spacer(1, 48 * mm))
    story.append(Paragraph("VPS Desk", styles["title"]))
    story.append(Paragraph("Manual de usuario", styles["title"]))
    story.append(Spacer(1, 5 * mm))
    story.append(
        Paragraph(
            "Guía para transportar la aplicación en una memoria USB, conectarse al servidor por SSH y usar la edición instalable.",
            styles["subtitle"],
        )
    )
    story.append(Spacer(1, 42 * mm))
    story.append(
        callout(
            "<b>Edición:</b> Windows x64<br/><b>Actualizado:</b> 15 de septiembre de 2026<br/>"
            "<b>Paquete:</b> app/clinical-care/appointments",
            styles["callout"],
            background=PALE,
            border=CYAN,
        )
    )
    story.append(PageBreak())
    doc.handle_nextPageTemplate("Body")

    story.append(Paragraph("Antes de comenzar", styles["h1"]))
    story.append(
        callout(
            "<b>Advertencia de seguridad.</b> Esta entrega contiene el archivo <b>.env</b>, una llave SSH privada y "
            "otros secretos reales. Quien tenga acceso a la USB puede intentar usarlos. Mantén la memoria cifrada, "
            "no la prestes y revoca las credenciales si se pierde.",
            styles["callout"],
            background=WARNING,
            border=WARNING_BORDER,
        )
    )
    story.append(Spacer(1, 5 * mm))
    story.append(Paragraph("Requisitos", styles["h2"]))
    for text in [
        "Una PC con Windows de 64 bits.",
        "Acceso a Internet o a la red donde se encuentre el VPS.",
        "Permiso del firewall para que VPS Desk establezca conexiones salientes SSH.",
        "La carpeta completa del paquete; no copies solamente el ejecutable.",
    ]:
        story.append(Paragraph(f"- {text}", styles["body"]))

    story.append(Paragraph("Contenido de la entrega", styles["h2"]))
    rows = [
        ["Elemento", "Uso"],
        ["VPS-Desk-portable-win-x64.zip", "Versión para copiar y ejecutar desde USB."],
        ["portable/", "Versión ya extraída, con datos y secretos actuales."],
        ["installer/VPS-Desk-Setup.exe", "Instalador para el usuario actual de Windows."],
        ["SHA256SUMS.txt", "Huellas para comprobar que los paquetes no cambiaron."],
        ["MANUAL-DE-USUARIO.pdf", "Este documento."],
    ]
    table = Table(wrap_table_rows(rows, styles), colWidths=[61 * mm, 105 * mm], repeatRows=1)
    table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), NAVY),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
                ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
                ("FONTNAME", (0, 1), (-1, -1), "Helvetica"),
                ("FONTSIZE", (0, 0), (-1, -1), 8.5),
                ("LEADING", (0, 0), (-1, -1), 11),
                ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#D5DDEA")),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT]),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ]
        )
    )
    story.append(table)
    story.append(Spacer(1, 6 * mm))
    story.append(Paragraph("Contenido", styles["h2"]))
    for item in [
        "1. Uso desde una memoria USB",
        "2. Conexión SSH incluida",
        "3. Instalación en otra PC",
        "4. Uso básico de VPS Desk",
        "5. Copias de seguridad y actualización",
        "6. Solución de problemas",
    ]:
        story.append(Paragraph(item, styles["toc"]))

    story.append(PageBreak())
    story.append(Paragraph("1. Uso desde una memoria USB", styles["h1"]))
    story.append(Paragraph("Preparar la memoria", styles["h2"]))
    story.append(step(1, "Copia la carpeta <b>app/clinical-care/appointments</b> completa a la USB.", styles))
    story.append(step(2, "En la otra PC, abre la USB y localiza <b>VPS-Desk-portable-win-x64.zip</b>.", styles))
    story.append(step(3, "Extrae el ZIP. No ejecutes la aplicación directamente desde la vista interna del ZIP.", styles))
    story.append(step(4, "Abre la carpeta extraída y ejecuta <b>VpsDesk.Desktop.exe</b>.", styles))
    story.append(step(5, "Si SmartScreen aparece, revisa que el archivo proceda de tu USB y usa la opción para ejecutarlo.", styles))

    story.append(Paragraph("Estructura que debes conservar", styles["h2"]))
    story.append(path_box("VpsDesk.Desktop.exe\n.env\nportable.mode\ndata/\nsecrets/id_vpsdesk", styles))
    story.append(Spacer(1, 4 * mm))
    story.append(
        Paragraph(
            "El archivo <b>portable.mode</b> hace que perfiles, preferencias e historial se guarden en <b>data</b> "
            "junto al programa. Esto evita dejar esa información en la PC prestada y permite continuar desde otra máquina.",
            styles["body"],
        )
    )
    story.append(
        callout(
            "<b>Antes de retirar la USB:</b> cierra VPS Desk, espera unos segundos y usa \"Expulsar\" en Windows. "
            "Así se reduce el riesgo de dañar la base de datos del historial.",
            styles["callout"],
        )
    )

    story.append(Paragraph("Dónde quedan los datos", styles["h2"]))
    story.append(path_box("portable/data/settings.json\nportable/data/servers.json\nportable/data/projects.json\nportable/data/deployments.json\nportable/data/vpsdesk.db", styles))

    story.append(PageBreak())
    story.append(Paragraph("2. Conexión SSH incluida", styles["h1"]))
    story.append(
        Paragraph(
            "VPS Desk incluye su propio cliente SSH mediante SSH.NET. No necesitas instalar PuTTY, OpenSSH ni copiar "
            "manualmente la llave al perfil de Windows.",
            styles["body"],
        )
    )
    story.append(Paragraph("Archivos utilizados", styles["h2"]))
    ssh_rows = [
        ["Archivo", "Función"],
        [".env", "Servidor, usuario, puerto, huella, rutas y secretos de arranque."],
        ["secrets/id_vpsdesk", "Llave SSH privada copiada desde la configuración actual."],
        ["secrets/id_vpsdesk.pub", "Llave pública, si estaba disponible al generar el paquete."],
    ]
    ssh_table = Table(wrap_table_rows(ssh_rows, styles), colWidths=[58 * mm, 108 * mm], repeatRows=1)
    ssh_table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), NAVY),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
                ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
                ("FONTNAME", (0, 1), (-1, -1), "Helvetica"),
                ("FONTSIZE", (0, 0), (-1, -1), 8.5),
                ("LEADING", (0, 0), (-1, -1), 11),
                ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#D5DDEA")),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT]),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 6),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 6),
            ]
        )
    )
    story.append(ssh_table)
    story.append(Spacer(1, 5 * mm))
    story.append(Paragraph("Ruta portable de la llave", styles["h2"]))
    story.append(path_box("SSH_KEY_PATH=secrets/id_vpsdesk", styles))
    story.append(
        Paragraph(
            "La ruta es relativa al archivo <b>.env</b>. Por eso continúa funcionando aunque Windows asigne otra letra a la USB.",
            styles["body"],
        )
    )
    story.append(Paragraph("Primera conexión", styles["h2"]))
    story.append(step(1, "Abre VPS Desk y entra en <b>Servidores</b>.", styles))
    story.append(step(2, "Confirma que host, usuario, puerto y tipo de autenticación sean correctos.", styles))
    story.append(step(3, "Conecta y verifica el panel principal antes de ejecutar despliegues o comandos.", styles))
    story.append(step(4, "Si aparece una diferencia en la huella del host, detente y comprueba el cambio con el administrador del VPS.", styles))
    story.append(
        callout(
            "Nunca reemplaces la huella SSH ni desactives su comprobación sólo para hacer desaparecer una alerta. "
            "Una huella distinta puede indicar un servidor reinstalado, una IP reutilizada o un ataque.",
            styles["callout"],
            background=WARNING,
            border=WARNING_BORDER,
        )
    )

    story.append(PageBreak())
    story.append(Paragraph("3. Instalación en otra PC", styles["h1"]))
    story.append(step(1, "Desde la USB, abre la carpeta <b>installer</b>.", styles))
    story.append(step(2, "Ejecuta <b>VPS-Desk-Setup.exe</b> y confirma la instalación.", styles))
    story.append(step(3, "El instalador crea accesos directos en el Escritorio y en el menú Inicio.", styles))
    story.append(step(4, "Cuando termine, permite que abra VPS Desk o usa cualquiera de los accesos directos.", styles))
    story.append(Paragraph("Ruta de instalación", styles["h2"]))
    story.append(path_box("%LOCALAPPDATA%/Programs/VPS Desk", styles))
    story.append(Paragraph("Datos de la edición instalada", styles["h2"]))
    story.append(path_box("%APPDATA%/VPS Desk\n%LOCALAPPDATA%/VPSDesk/vpsdesk.db", styles))
    story.append(
        Paragraph(
            "A diferencia de la edición portable, la versión instalada guarda datos en el perfil de Windows. "
            "Los datos permanecen aunque retires la USB.",
            styles["body"],
        )
    )
    story.append(Paragraph("Desinstalación", styles["h2"]))
    story.append(
        Paragraph(
            "Abre <b>Configuración de Windows - Aplicaciones - Aplicaciones instaladas</b>, busca VPS Desk y elige "
            "Desinstalar. Los datos del usuario se conservan para evitar pérdidas accidentales.",
            styles["body"],
        )
    )

    story.append(Paragraph("4. Uso básico de VPS Desk", styles["h1"]))
    usage = [
        ["Área", "Qué permite hacer"],
        ["Panel", "Revisar estado, métricas y capacidad del VPS."],
        ["Servidores", "Administrar perfiles y establecer la conexión SSH."],
        ["Proyectos", "Asociar repositorios remotos, ramas, Compose y archivos .env."],
        ["Despliegues", "Actualizar repositorios y ejecutar despliegues Docker Compose."],
        ["Contenedores", "Consultar y operar contenedores del servidor."],
        ["Archivos", "Examinar y editar archivos remotos mediante SFTP."],
        ["Terminal", "Abrir una terminal interactiva en el VPS."],
        ["Seguridad", "Ejecutar comprobaciones de seguridad del servidor."],
    ]
    usage_table = Table(wrap_table_rows(usage, styles), colWidths=[43 * mm, 123 * mm], repeatRows=1)
    usage_table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), NAVY),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
                ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
                ("FONTNAME", (0, 1), (0, -1), "Helvetica-Bold"),
                ("FONTNAME", (1, 1), (-1, -1), "Helvetica"),
                ("FONTSIZE", (0, 0), (-1, -1), 8.4),
                ("LEADING", (0, 0), (-1, -1), 10.5),
                ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#D5DDEA")),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT]),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
            ]
        )
    )
    story.append(usage_table)

    story.append(PageBreak())
    story.append(Paragraph("5. Copias de seguridad y actualización", styles["h1"]))
    story.append(Paragraph("Copia cifrada con contraseña", styles["h2"]))
    story.append(step(1, "Abre <b>Configuración</b> y busca la sección <b>Copia cifrada</b>.", styles))
    story.append(step(2, "Elige <b>Crear copia cifrada</b>, define dónde guardar el archivo <b>.vpsbackup</b> y crea una contraseña de al menos 12 caracteres.", styles))
    story.append(step(3, "Guarda esa contraseña fuera de la USB. VPS Desk no la almacena ni puede recuperarla.", styles))
    story.append(step(4, "Para recuperar una copia, usa <b>Restaurar copia cifrada</b>, selecciona el archivo, escribe su contraseña y reinicia VPS Desk.", styles))
    story.append(
        Paragraph(
            "La copia incluye los perfiles, preferencias, historial, archivo <b>.env</b> y la carpeta <b>secrets</b>. "
            "Su contenido se cifra localmente con AES-256-GCM y la contraseña se deriva con PBKDF2.",
            styles["body"],
        )
    )
    story.append(Paragraph("Copia de seguridad portable", styles["h2"]))
    story.append(step(1, "Cierra VPS Desk.", styles))
    story.append(step(2, "Copia las carpetas <b>data</b> y <b>secrets</b>, además del archivo <b>.env</b>, a un almacenamiento cifrado.", styles))
    story.append(step(3, "Verifica que la copia pueda abrirse antes de borrar una versión anterior.", styles))
    story.append(Paragraph("Actualizar el paquete", styles["h2"]))
    story.append(
        Paragraph(
            "El paquete se regenera desde el repositorio ejecutando <b>Scripts/Build-Distributions.ps1</b>. "
            "El proceso vuelve a copiar la configuración, los datos y la llave indicados en el entorno actual.",
            styles["body"],
        )
    )
    story.append(
        callout(
            "El generador reemplaza la carpeta de entrega. Guarda primero cualquier dato portable más reciente que sólo exista en la USB.",
            styles["callout"],
            background=WARNING,
            border=WARNING_BORDER,
        )
    )
    story.append(Paragraph("Comprobar integridad", styles["h2"]))
    story.append(
        Paragraph(
            "Usa <b>SHA256SUMS.txt</b> para comparar la huella del ZIP y del instalador. En PowerShell puedes ejecutar:",
            styles["body"],
        )
    )
    story.append(path_box("Get-FileHash .\\VPS-Desk-portable-win-x64.zip -Algorithm SHA256\nGet-FileHash .\\installer\\VPS-Desk-Setup.exe -Algorithm SHA256", styles))

    story.append(Paragraph("6. Solución de problemas", styles["h1"]))
    problems = [
        ["Problema", "Comprobación recomendada"],
        ["Windows bloquea el ejecutable", "Comprueba el origen, abre Propiedades y revisa la opción Desbloquear. El paquete no está firmado digitalmente."],
        ["No conecta por SSH", "Revisa red, firewall, host, puerto, usuario, huella y que secrets/id_vpsdesk exista."],
        ["No encuentra la llave", "Confirma que .env y secrets estén junto al ejecutable y que SSH_KEY_PATH conserve la ruta relativa."],
        ["La USB cambió de letra", "No requiere cambios: la ruta de la llave y el directorio de datos son relativos."],
        ["No guarda cambios", "Comprueba que la USB tenga espacio y no esté protegida contra escritura."],
        ["El historial parece antiguo", "Cierra la copia abierta en otra PC y confirma que estás usando la misma carpeta portable."],
        ["La huella SSH cambió", "No continúes hasta validar la nueva huella por un canal confiable."],
    ]
    problems_table = Table(wrap_table_rows(problems, styles), colWidths=[53 * mm, 113 * mm], repeatRows=1)
    problems_table.setStyle(
        TableStyle(
            [
                ("BACKGROUND", (0, 0), (-1, 0), NAVY),
                ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
                ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
                ("FONTNAME", (0, 1), (0, -1), "Helvetica-Bold"),
                ("FONTNAME", (1, 1), (-1, -1), "Helvetica"),
                ("FONTSIZE", (0, 0), (-1, -1), 8.2),
                ("LEADING", (0, 0), (-1, -1), 10.5),
                ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#D5DDEA")),
                ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, LIGHT]),
                ("VALIGN", (0, 0), (-1, -1), "TOP"),
                ("LEFTPADDING", (0, 0), (-1, -1), 6),
                ("RIGHTPADDING", (0, 0), (-1, -1), 6),
                ("TOPPADDING", (0, 0), (-1, -1), 5),
                ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
            ]
        )
    )
    story.append(problems_table)

    doc.build(story)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Uso: python Generate-Manual.py <ruta-pdf>")
    make_manual(Path(sys.argv[1]).resolve())
