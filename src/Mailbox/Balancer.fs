module Mailbox.Balancer
open Mailbox.Helpers

let evaluateSparsity (mtx: int[,]) =
    let mutable nons = 0.
    let numOfElems = float <| mtx.GetLength(0) * mtx.GetLength(1)
    for i in 0 .. mtx.GetLength(0) - 1 do
        for j in 0 .. mtx.GetLength(1) - 1 do
            if mtx.[i, j] = 0
            then nons <- nons + 1.
    nons / numOfElems

let rateMatricesOnSparsity fst snd =
    let sparsityA = evaluateSparsity fst
    let sparsityB = evaluateSparsity snd
    let totalSize = fst.GetLength(0) + fst.GetLength(1) + snd.GetLength(0) + snd.GetLength(1)
    if (sparsityA > 0.75 || sparsityB > 0.75) && (totalSize < 16) then QtDefault
    elif (sparsityA > 0.75 || sparsityB > 0.75) && (totalSize >= 16) then QtParallel
    elif (sparsityA <= 0.75 && sparsityB <= 0.75) && (totalSize < 16) then ArrDefault
    else ArrParallel

let balancer (qtMultiply: MailboxProcessor<messageBalancer>) (qtParallelMultiply: MailboxProcessor<messageBalancer>)
             (arrMultiply: MailboxProcessor<messageBalancer>) (arrParallelMultiply: MailboxProcessor<messageBalancer>) =

    MailboxProcessor.Start(fun inbox ->
        let rec loop () =
            async {
                let! msg = inbox.Receive()
                match msg with
                | messageBalancer.EOS ch ->
                    qtMultiply.PostAndReply messageBalancer.EOS
                    arrMultiply.PostAndReply messageBalancer.EOS
                    qtParallelMultiply.PostAndReply messageBalancer.EOS
                    arrParallelMultiply.PostAndReply messageBalancer.EOS
                    ch.Reply()
                | PairOfMatrices ((fst, _), (snd, _)) as tuple ->
                    match rateMatricesOnSparsity fst snd with
                        | QtParallel -> qtParallelMultiply.Post tuple
                        | QtDefault -> qtMultiply.Post tuple
                        | ArrDefault -> arrMultiply.Post tuple
                        | ArrParallel -> arrParallelMultiply.Post tuple
                    return! loop ()
            }
        loop ()
    )

