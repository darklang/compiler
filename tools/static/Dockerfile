# Dockerfile - Alpine toolchain for a static musl darkc.
FROM alpine:3.22
ARG OCAML_VERSION=5.5.1
ARG OCAML_CONFIGURE=
RUN apk add --no-cache build-base bash curl git m4 perl python3 pkgconf linux-headers \
      opam gmp-dev sqlite-dev sqlite-static zstd xz binutils
WORKDIR /ocaml-build
RUN curl --fail --location --retry 2 \
      "https://github.com/ocaml/ocaml/archive/refs/tags/${OCAML_VERSION}.tar.gz" --output ocaml.tar.gz && \
    echo "ae47728d0a876665ccc7b745e3a8d8c9ca5ba63c4f7a11d4db6cdbd1dbeef4bb  ocaml.tar.gz" | sha256sum -c && \
    tar xzf ocaml.tar.gz --strip-components=1 && \
    ./configure --prefix=/opt/ocaml $OCAML_CONFIGURE && make -j8 world.opt && make install && \
    cd / && rm -rf /ocaml-build
RUN adduser -D -u 1000 agent
USER agent
ENV HOME=/home/agent
ENV PATH=/home/agent/.opam/dark/bin:/opt/ocaml/bin:$PATH
ENV OPAM_SWITCH_PREFIX=/home/agent/.opam/dark OCAMLPATH=/home/agent/.opam/dark/lib
ENV CAML_LD_LIBRARY_PATH=/home/agent/.opam/dark/lib/stublibs:/opt/ocaml/lib/ocaml/stublibs
RUN opam init --bare --disable-sandboxing --yes && opam switch create dark ocaml-system.${OCAML_VERSION} --yes
COPY dependencies.lock /tmp/dependencies.lock
RUN sed "/^[[:space:]]*#/d; /^[[:space:]]*$/d; /^ocamlformat/d; /^ocp-indent/d; /^dune-compiledb/d" /tmp/dependencies.lock | \
    xargs opam install --switch=dark --yes --jobs=8 --no-depexts
USER root
RUN apk add --no-cache gmp-static
USER agent
WORKDIR /work
