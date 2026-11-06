# Pizarra

Pizarra digital para Windows con interfaz oscura de cristal y neón: dibuja encima de la pantalla, en hojas independientes o escribe con el teclado.
Es un solo `.exe` sin instalación; usa .NET Framework 4.8, ya incluido en Windows 10 y 11.

## Descargar

[Descargar Pizarra.exe](https://github.com/VLADIMIR1991-05/Pizarra_Vladd/raw/main/dist/Pizarra.exe)

El programa no está firmado, así que Windows puede avisar al abrirlo: pulsa **Más información** y luego **Ejecutar de todas formas**.

## Modos

- **Pantalla:** dibuja sobre cualquier programa. Con el puntero (`Esc` o `Ctrl+Alt+D`) sigues usando el equipo sin borrar el dibujo.
- **Hojas:** varias hojas con zoom y desplazamiento, sobre una superficie de 16000 × 12000 puntos.
- **Escribir:** una página de texto lista para teclear, con formato.

## Funciones pro

| Función | Cómo se usa |
|---|---|
| Paleta de comandos | `Ctrl+K`: busca y ejecuta cualquier acción escribiendo, sin importar los acentos |
| Menú radial | Botón derecho sobre el lienzo: clic en un sector, o arrastrar y soltar sobre él |
| Favoritos | 5 lápices con su color, grosor y modo de trazo: `Ctrl+1` a `Ctrl+5`; `Ctrl+Mayús+1` a `5` o botón derecho guardan el actual |
| Miniaturas | `F2`: panel lateral con todas las hojas para saltar entre ellas |
| Minimapa | Vista general de la hoja; clic o arrastre para moverse. `Ctrl+0` ajusta la vista al contenido |
| Presentación | `F5`: pantalla limpia, flechas para cambiar de hoja y cronómetro |
| Repetición | Reproduce el dibujo trazo a trazo, sin modificar la hoja. Muestra solo los trazos, no textos ni imágenes |
| Grabar vídeo | Botón **REC** o `Ctrl+Alt+R`: tras una cuenta atrás, graba el monitor con el cursor en MP4 (H.264, 30 fps) en `Vídeos\Pizarra`. La barra de herramientas no sale en el vídeo. Sin sonido |
| Captura de región | Congela la pantalla, se arrastra un recuadro y el recorte va al portapapeles y a una hoja nueva |
| Exportar SVG | Vectorial, con trazos, texto e imágenes. Neón, plasma y aerosol se simplifican |
| Selección | `Ctrl+A` todo, `Ctrl+D` duplicar y alinear en 6 direcciones desde el botón **PRO** |

Además, la interfaz se atenúa mientras dibujas para no tapar el trazo. El botón **PRO** de la barra reúne estas funciones.

## Funciones

| Área | Incluye |
|---|---|
| Lápices | Bolígrafo, lápiz, rotulador, pincel, neón, plasma, discontinuo, punteado, aerosol, arcoíris y tinta que se desvanece |
| Estilos de escritura | Caligrafía itálica y gótica con ángulo de plumilla ajustable, letra inglesa, estilográfica, firma y cepillo; pauta de caligrafía como fondo |
| Modos de trazo | Dibujo libre, suavizado con nivel ajustable, línea recta, ortogonal a mano, geometría, letras a texto, polilínea, polilínea ortogonal y curva por puntos; ajuste a la rejilla |
| Reconocimiento | Letras a mano a texto con el reconocedor de Windows, con panel de corrección y alternativas; ortografía al editar; dibujos a línea, flecha, elipse, triángulo, rectángulo, rombo, pentágono, hexágono o estrella |
| Otras herramientas de dibujo | Resaltador, borrador parcial y de trazo completo, selección para mover y redimensionar; convertir o suavizar lo ya dibujado |
| Color | 10 colores rápidos, 48 colores ampliados, selector personalizado, cuentagotas y opacidad |
| Formas | Línea, flecha, flecha doble, rectángulo, rectángulo redondeado, elipse, triángulos, rombo, pentágono, hexágono, estrella y ejes; con relleno opcional |
| Texto | Cuadros de texto, notas adhesivas, 15 tipos de letra, tamaño, color, negrita, cursiva, subrayado, tachado, resaltado, alineación, listas y sangría |
| Hojas | 12 fondos (cyber, plano técnico, blanca, cuadriculada, rayada, puntos, negra, verde, milimetrada, pentagrama, isométrica, pauta), duplicar, reordenar y plantillas (Kanban, DAFO, Cornell, plan semanal) |
| Imágenes | Insertar archivos, pegar con `Ctrl+V` y capturar la pantalla en una hoja nueva |
| Geometría | Regla con ajuste del lápiz a su borde y transportador |
| Presentar | Puntero láser, foco y lupa |
| Guardar | Cuaderno `.pizarra`, PNG, JPEG, PDF con todas las hojas y autoguardado cada minuto |

Pulsa `F1` o el botón **?** de la barra para ver todos los atajos de teclado.

## Compilar

No hace falta instalar nada: el compilador viene con Windows.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Pruebas

```powershell
powershell -ExecutionPolicy Bypass -File tests\run.ps1
```

Compila una versión de prueba, la ejecuta con las ventanas invisibles y guarda el resultado en `tests\salida\resultado.txt`.
Las pruebas llaman directamente al código: no sustituyen a probar con un ratón o lápiz real.

## No incluido

Capas, colaboración en tiempo real, nube, importar PDF y sonido en los vídeos.

La conversión de letras a texto necesita el paquete de escritura a mano de Windows para el idioma; si falta, la aplicación indica dónde instalarlo.
