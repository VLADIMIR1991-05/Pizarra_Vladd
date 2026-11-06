# Pizarra

Pizarra digital para Windows: dibuja encima de la pantalla, en hojas independientes o escribe con el teclado.
Es un solo `.exe` sin instalación; usa .NET Framework 4.8, ya incluido en Windows 10 y 11.

## Descargar

[Descargar Pizarra.exe](https://github.com/VLADIMIR1991-05/Pizarra_Vladd/raw/main/dist/Pizarra.exe)

El programa no está firmado, así que Windows puede avisar al abrirlo: pulsa **Más información** y luego **Ejecutar de todas formas**.

## Modos

- **Pantalla:** dibuja sobre cualquier programa. Con el puntero (`Esc` o `Ctrl+Alt+D`) sigues usando el equipo sin borrar el dibujo.
- **Hojas:** varias hojas con zoom y desplazamiento, sobre una superficie de 16000 × 12000 puntos.
- **Escribir:** una página de texto lista para teclear, con formato.

## Funciones

| Área | Incluye |
|---|---|
| Lápices | Bolígrafo, lápiz, rotulador, pincel, neón, discontinuo, punteado, aerosol, arcoíris y tinta que se desvanece |
| Estilos de escritura | Caligrafía itálica y gótica con ángulo de plumilla ajustable, letra inglesa, estilográfica, firma y cepillo; pauta de caligrafía como fondo |
| Modos de trazo | Dibujo libre, suavizado con nivel ajustable, línea recta, ortogonal a mano, geometría, letras a texto, polilínea, polilínea ortogonal y curva por puntos; ajuste a la rejilla |
| Reconocimiento | Letras a mano a texto con el reconocedor de Windows, con panel de corrección y alternativas; ortografía al editar; dibujos a línea, flecha, elipse, triángulo, rectángulo, rombo, pentágono, hexágono o estrella |
| Otras herramientas de dibujo | Resaltador, borrador parcial y de trazo completo, selección para mover y redimensionar; convertir o suavizar lo ya dibujado |
| Color | 10 colores rápidos, 48 colores ampliados, selector personalizado, cuentagotas y opacidad |
| Formas | Línea, flecha, flecha doble, rectángulo, rectángulo redondeado, elipse, triángulos, rombo, pentágono, hexágono, estrella y ejes; con relleno opcional |
| Texto | Cuadros de texto, notas adhesivas, 15 tipos de letra, tamaño, color, negrita, cursiva, subrayado, tachado, resaltado, alineación, listas y sangría |
| Hojas | 10 fondos (blanca, cuadriculada, rayada, puntos, negra, verde, milimetrada, pentagrama, isométrica, pauta), duplicar, reordenar y plantillas (Kanban, DAFO, Cornell, plan semanal) |
| Imágenes | Insertar archivos, pegar con `Ctrl+V` y capturar la pantalla en una hoja nueva |
| Geometría | Regla con ajuste del lápiz a su borde y transportador |
| Presentar | Puntero láser, foco y lupa |
| Guardar | Cuaderno `.pizarra`, PNG, JPEG, PDF con todas las hojas y autoguardado cada minuto |

Pulsa el botón **?** de la barra para ver todos los atajos de teclado.

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

Colaboración en tiempo real, nube, importar PDF y grabar vídeo.

La conversión de letras a texto necesita el paquete de escritura a mano de Windows para el idioma; si falta, la aplicación indica dónde instalarlo.
