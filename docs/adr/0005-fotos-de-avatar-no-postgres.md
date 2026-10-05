# ADR-0005 — Fotos de avatar: processadas no servidor e guardadas no PostgreSQL

- **Status:** Aceito
- **Data:** 2026-10-04

## Contexto

Cada pessoa (e, em breve, cada membro de grupo sem conta) pode usar um avatar pronto **ou subir uma foto própria**. O produto é para amigos e família, então as fotos são pessoais. A hospedagem é gratuita: o disco do Render é efêmero, não há armazenamento de objetos contratado, e o banco (Supabase) já é o único estado durável. Fotos de celular chegam com 3 a 12 MB, EXIF com localização e, às vezes, conteúdo malicioso disfarçado de imagem.

## Decisão

- **O servidor processa toda foto e nunca guarda o original.** O arquivo é recusado se o conteúdo não for JPEG, PNG, WebP ou GIF (o tipo é decidido pelo conteúdo, nunca pelo nome ou `Content-Type`); o cabeçalho é lido antes de decodificar (lado máximo de 8000 px e 25 megapixels) para barrar imagens "bomba"; a imagem é orientada pelo EXIF, **recortada em quadrado pelo centro, reduzida para 256×256 e reencodada em WebP** (qualidade 80). Reencodar descarta EXIF/GPS, XMP, perfis e qualquer carga escondida.
- **Fica no PostgreSQL** (`app.avatars`, coluna `bytea`): poucos KB por foto, transacional com o perfil, sem serviço extra, e já coberto pelo backup do banco. Cada foto tem id UUID não adivinhável; trocar a foto cria outra linha e apaga a anterior quando ninguém mais aponta para ela.
- **É servida pela API**, `GET /api/v1/avatars/{id}`, sem autenticação (para funcionar num `<img>`), com cache imutável de um ano, `ETag`, `nosniff` e CSP restritiva. O endereço muda a cada troca, então não há invalidação de cache.
- **Biblioteca: SkiaSharp** (MIT) com binários nativos sem dependências do sistema (`NoDependencies` para Linux, o que funciona no contêiner enxuto do Render). `SixLabors.ImageSharp` 4 foi descartada: exige chave de licença, inclusive para compilar, o que impediria colaboradores e o CI de construir o projeto.
- **Limites de custo:** 3 MB por envio, 2 decodificações simultâneas, 20 envios por hora por pessoa. O CHECK do banco limita a foto final a 512 KB (na prática, bem menos).

## Consequências

- (+) Um único lugar com estado; backup e restauração do banco levam as fotos junto; nada de armazenamento de objetos para configurar ou pagar.
- (+) A foto "limpa" por construção: não depende de lembrar de remover metadados.
- (+) Trocar para armazenamento de objetos (R2/S3) no futuro é localizado: `AvatarService` e o endpoint de leitura.
- (−) As fotos ocupam o banco gratuito (o plano gratuito do Supabase tem 500 MB). Com algo da ordem de 10 KB por foto, cabem dezenas de milhares; se o espaço apertar, migra-se para armazenamento de objetos.
- (−) Decodificar imagens no servidor gasta CPU e memória numa instância pequena; mitigado pelos limites acima.
- (−) Os decodificadores nativos do Skia são superfície de ataque; mitigado por validação prévia, nada do original ser repassado e atualização contínua do pacote (ver `docs/SECURITY.md`).
- O avatar pronto (`preset-N`) não passa por nada disso: as imagens vivem no Front e a API só guarda a chave.
